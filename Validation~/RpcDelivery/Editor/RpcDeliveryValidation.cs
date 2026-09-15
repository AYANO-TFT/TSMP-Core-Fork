using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
#endif
using Object = UnityEngine.Object;

public static class RpcDeliveryValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string MethodName = nameof(TSMPNetworkGameObjectToggle.ToggleObject);
    private static readonly uint RpcHash = StableHash.Fnv1A32(MethodName);
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Test("Decoder cross-stream Toggle delivery and repeated-frame suppression", CrossStreamDelivery);
        Test("Decoder key dimensions and legacy events", DedupKeys);
#if !UDONSHARP
        Test("Native codec failure preserves single-send RPC", () => CodecFailure(false));
        Test("Native codec exception preserves single-send RPC", () => CodecFailure(true));
        Test("Native repeats and FIFO survive intermittent codec failure", RepeatsAndOrder);
        Test("Native capacity and argument failures preserve queue", BuildFailure);
        Test("RPC appended by codec is not consumed by variable-only frame", LateEnqueue);
#else
        Results.Add("Native Encoder tests excluded: this project uses the Udon Encoder path");
#endif
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        File.WriteAllText(output, (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion +
            "\nGraphics=" + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + "\n" + string.Join("\n", Results));
        if (failed) throw new InvalidOperationException("RPC delivery validation failed; see result file");
    }

    private static T Add<T>(GameObject owner) where T : TSMPBehaviour
    {
#if UDONSHARP
        return owner.AddUdonSharpComponent<T>();
#else
        return owner.AddComponent<T>();
#endif
    }

    private sealed class Receiver : IDisposable
    {
        private readonly GameObject owner = new GameObject("RPC Receiver");
        public readonly GameObject Target = new GameObject("Toggle Target");
        public readonly TSMPDecoder Decoder;

        public Receiver()
        {
            var toggle = Add<TSMPNetworkGameObjectToggle>(owner);
            toggle.networkId = 1;
            toggle.targetObject = Target;
            Decoder = Add<TSMPDecoder>(owner);
            Decoder.applyEveryFrame = false;
            Decoder.debugLog = false;
            Decoder.bindingTargets = new Component[] { toggle, toggle };
            Decoder.bindingNetworkIds = new ushort[] { 1, 1 };
#if UDONSHARP
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(toggle);
            Decoder.bindingUdonTargets = new[] { backing, backing };
#endif
        }

        public void Apply(byte[] payload, uint stream, bool duplicate)
        {
            bool wasActive = Target.activeSelf;
            Decoder.lastStreamId = stream;
            typeof(TSMPDecoder).GetField("_payloadBytes", Private).SetValue(Decoder, payload);
            Check((bool)typeof(TSMPDecoder).GetMethod("ApplyNetworkFrame", Private).Invoke(Decoder, null), Decoder.lastError);
            Check(Decoder.lastRpcCallCount == (duplicate ? 0 : 1), "Unexpected dispatched RPC count for stream " + stream);
            Check(Decoder.skippedDuplicateRpcCount == (duplicate ? 1 : 0), "Unexpected duplicate count");
            Check(Target.activeSelf == (duplicate ? wasActive : !wasActive), "Toggle was lost or dispatched twice");
        }

        public void Dispose()
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(Target);
        }
    }

    private static void CrossStreamDelivery()
    {
        using (var receiver = new Receiver())
        {
            byte[] frame = RpcFrame(77);
            receiver.Apply(frame, 1, false);
            receiver.Apply(frame, 1, true);
            receiver.Apply(frame, 2, false);
            receiver.Apply(frame, 2, true);
            receiver.Apply(frame, 1, true);
            receiver.Apply(frame, 0, false);
            receiver.Apply(frame, uint.MaxValue, false);
            receiver.Apply(frame, 0, true);
            receiver.Apply(RpcFrame(78), 1, false);
            receiver.Apply(RpcFrame(78), 1, true);
        }
    }

    private static void DedupKeys()
    {
        using (var receiver = new Receiver())
        {
            TSMPDecoder decoder = receiver.Decoder;
            MethodInfo method = typeof(TSMPDecoder).GetMethod("IsDuplicateRpcEvent", Private);
            Func<uint, ushort, uint, int, bool> duplicate = (stream, network, hash, id) =>
            {
                decoder.lastStreamId = stream;
                return (bool)method.Invoke(decoder, new object[] { network, hash, id });
            };
            Check(!duplicate(1, 1, 100, 1), "First key was duplicate");
            Check(duplicate(1, 1, 100, 1), "Repeat not suppressed");
            Check(!duplicate(1, 2, 100, 1), "Network ID ignored");
            Check(!duplicate(1, 1, 101, 1), "Method hash ignored");
            Check(!duplicate(1, 1, 100, 2), "Event ID ignored");
            Check(!duplicate(1, 1, 100, 0) && !duplicate(1, 1, 100, 0), "Legacy zero ID was deduplicated");
            Check(!duplicate(1, 1, 100, -1) && !duplicate(1, 1, 100, -1), "Legacy negative ID was deduplicated");
            for (int i = 0; i < 32; i++) Check(!duplicate(1, 1, 100, i + 100), "Unique event discarded");
            Check(!duplicate(1, 1, 100, 1), "Bounded cache eviction changed");
        }
    }

    private static byte[] RpcFrame(int eventId)
    {
        byte[] payload = new byte[256];
        int cursor = NetworkFrameWriter.BeginNetworkFrame(payload, 0, 1);
        cursor = NetworkFrameWriter.WriteRpcCall(payload, cursor, 1, 1, RpcHash,
            new object[] { MethodName, eventId }, out int failedArgument, out int error);
        Check(cursor >= 0, "RPC fixture write failed: " + failedArgument + "/" + error);
        Check(NetworkFrameWriter.EndNetworkFrame(payload, 0, 1), "RPC fixture finish failed");
        Array.Resize(ref payload, cursor);
        return payload;
    }

#if !UDONSHARP
    private sealed class Sender : IDisposable
    {
        private readonly GameObject owner = new GameObject("RPC Sender");
        private readonly GameObject codecOwner;
        private readonly RenderTexture output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
        public readonly TSMPEncoder Encoder;
        public readonly RpcDeliveryFaultCodec Codec;
        public List<EncoderNativeFrameBuilder.QueuedRpc> Queue =>
            (List<EncoderNativeFrameBuilder.QueuedRpc>)typeof(TSMPEncoder).GetField("_queuedRpcs", Private).GetValue(Encoder);
        public byte[] Payload => (byte[])typeof(TSMPEncoder).GetField("_encodedPayload", Private).GetValue(Encoder);

        public Sender()
        {
            codecOwner = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"));
            Codec = owner.AddComponent<RpcDeliveryFaultCodec>();
            Codec.realCodec = codecOwner.GetComponent<TSMPCodec>();
            Check(Codec.realCodec != null, "Real Luma4 codec missing");
            Codec.codecId = Codec.realCodec.codecId;
            Encoder = owner.AddComponent<TSMPEncoder>();
            Encoder.autoEncode = false;
            Encoder.debugLog = false;
            Encoder.debugErrorLogBudget = 0;
            Encoder.output = output;
            output.Create();
            Encoder.selectedCodec = Codec;
            Encoder.transRpcRepeatFrames = 1;
        }

        public void Enqueue(int eventId)
        {
            Check(Encoder.QueueTransRpc(1, RpcHash, MethodName, eventId), "Failed to enqueue fixture RPC");
        }

        public void Encode()
        {
            uint before = Encoder.frameIndex;
            Encoder.EncodeNow();
            Check(Encoder.frameIndex == before + 1 && string.IsNullOrEmpty(Encoder.lastError), "Frame write failed: " + Encoder.lastError);
            byte[] bytes = (byte[])typeof(TSMPEncoder).GetField("_header", Private).GetValue(Encoder);
            Check(FrameHeader.TryRead(bytes, 0, out FrameHeader header) && header.FrameIndex == before &&
                header.PayloadSize == Payload.Length, "Written frame header is invalid");
            Check(Encoder.queuedRpcCount == Queue.Count, "Queue diagnostics not updated after output");
        }

        public void Dispose()
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(codecOwner);
            if (RenderTexture.active == output) RenderTexture.active = null;
            output.Release();
            Object.DestroyImmediate(output);
        }
    }

    private static void CodecFailure(bool throws)
    {
        using (var sender = new Sender())
        using (var receiver = new Receiver())
        {
            sender.Enqueue(77);
            sender.Codec.fail = !throws;
            sender.Codec.throwOnWrite = throws;
            try { sender.Encoder.EncodeNow(); Check(!throws, "Injected codec exception did not occur"); }
            catch (InvalidOperationException exception) when (throws && exception.Message == "Injected RPC codec exception") { }
            Check(sender.Encoder.frameIndex == 0, "Failed output advanced frame index");
            Results.Add("After codec failure: queued=" + sender.Queue.Count);
            Check(sender.Queue.Count == 1 && sender.Queue[0].RepeatsRemaining == 1, "Failed output consumed RPC");
            sender.Codec.fail = false;
            sender.Codec.throwOnWrite = false;
            sender.Encode();
            Check(sender.Queue.Count == 0, "Successful output did not commit RPC");
            receiver.Apply(sender.Payload, 1, false);
        }
    }

    private static void RepeatsAndOrder()
    {
        using (var sender = new Sender())
        using (var receiver = new Receiver())
        {
            sender.Encoder.transRpcRepeatFrames = 3;
            sender.Enqueue(77);
            sender.Enqueue(78);
            for (int i = 0; i < 6; i++)
            {
                var head = sender.Queue[0];
                uint before = sender.Encoder.frameIndex;
                sender.Codec.fail = true;
                sender.Encoder.EncodeNow();
                Check(sender.Queue.Count == (i < 3 ? 2 : 1) && sender.Queue[0].RepeatsRemaining == head.RepeatsRemaining,
                    "Failed write consumed a repeat");
                Check(sender.Encoder.frameIndex == before, "Failure advanced frame index");
                sender.Codec.fail = false;
                sender.Encode();
                receiver.Apply(sender.Payload, 1, i % 3 != 0);
                Check(receiver.Decoder.lastRpcEventId == (i < 3 ? 77 : 78), "FIFO/event identity changed");
            }
            Check(sender.Queue.Count == 0 && receiver.Target.activeSelf, "Both events were not delivered exactly once in fixture");
        }
    }

    private static void BuildFailure()
    {
        using (var sender = new Sender())
        {
            sender.Enqueue(77);
            sender.Codec.capacity = 1;
            sender.Encoder.EncodeNow();
            Check(sender.Encoder.frameIndex == 0 && !string.IsNullOrEmpty(sender.Encoder.lastError), "Capacity failure missing");
            Check(sender.Queue.Count == 1 && sender.Queue[0].RepeatsRemaining == 1, "Capacity failure consumed RPC");
            sender.Codec.capacity = -1;
            sender.Encode();
            object[] arguments = { new object() };
            Check(sender.Encoder.QueueRpcHash(1, RpcHash, arguments), "Legacy RPC queue failed");
            sender.Encoder.EncodeNow();
            Check(sender.Encoder.frameIndex == 1 && !string.IsNullOrEmpty(sender.Encoder.lastError), "Invalid argument did not fail serialization");
            Check(sender.Queue.Count == 1 && sender.Queue[0].RepeatsRemaining == 1, "Serialization failure consumed RPC");
            arguments[0] = MethodName;
            sender.Encode();
            Check(sender.Queue.Count == 0, "Corrected RPC did not commit");
        }
    }

    private static void LateEnqueue()
    {
        using (var sender = new Sender())
        {
            var source = new GameObject("Variable Source");
            try
            {
                var sync = source.AddComponent<TSMPNetworkTransformSync>();
                sync.networkId = 2;
                sync.target = source.transform;
                sender.Encoder.networkBehaviours = new TSMPNetworkBehaviour[] { sync };
                sender.Codec.beforeWrite = () => sender.Enqueue(77);
                sender.Encode();
                Check(sender.Encoder.rpcMessageCount == 0 && sender.Encoder.variableMessageCount == 1, "Expected variable-only frame");
                Check(sender.Queue.Count == 1 && sender.Queue[0].RepeatsRemaining == 1, "Unsent late RPC consumed");
                sender.Codec.beforeWrite = null;
                source.transform.localPosition = Vector3.one;
                sender.Encode();
                Check(sender.Encoder.rpcMessageCount == 1 && sender.Encoder.variableMessageCount == 1 && sender.Queue.Count == 0,
                    "Mixed variable/RPC frame did not commit queued event");
            }
            finally { Object.DestroyImmediate(source); }
        }
    }
#endif

    private static void Test(string name, Action run)
    {
        try { run(); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
