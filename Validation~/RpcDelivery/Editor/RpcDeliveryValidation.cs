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
        Test("Native unavailable frame capacity preserves queue", BuildFailure);
        Test("Native invalid RPC arguments are rejected before enqueue", InvalidArguments);
        Test("Native supported RPC arguments and wire-size boundaries", ArgumentBoundaries);
        Test("Native capacity reduction drops only the unsendable head", CapacityReduction);
        Test("Native arguments mutated after enqueue cannot block the queue", MutatedArguments);
        Test("Native capture-time RPC enqueue preserves its full repeat budget", CaptureEnqueue);
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
        }
    }

    private static void InvalidArguments()
    {
        using (var sender = new Sender())
        {
            foreach (object[] arguments in new[]
            {
                new object[] { new object() }, new object[] { null }, new object[256],
                new object[] { new byte[65536] }, new object[] { new string('a', 65536) },
                new object[] { new string[65536] }
            })
            {
                Check(!sender.Encoder.QueueRpcHash(1, RpcHash, arguments), "Invalid RPC was accepted");
                Check(sender.Queue.Count == 0 && sender.Encoder.queuedRpcCount == 0, "Rejected RPC entered queue");
                Check(sender.Encoder.lastError.StartsWith("RPC rejected: networkId=1, hash="), "Rejection identity missing");
            }
            Check(sender.Encoder.lastError.Contains("index 0"), "Argument index missing");
            sender.Codec.capacity = 64;
            Check(!sender.Encoder.QueueRpc(1, MethodName, new byte[64]), "Oversized RPC accepted");
            Check(sender.Encoder.lastError.Contains("capacity=64"), "Capacity diagnosis missing");
            Check(!sender.Encoder.QueueTransRpc(1, RpcHash, new string('x', 80)), "Oversized TransRPC accepted");
            sender.Enqueue(77);
            sender.Encode();
            Check(sender.Queue.Count == 0, "Rejected RPC stopped next valid RPC");
        }
    }

    private static void ArgumentBoundaries()
    {
        using (var sender = new Sender())
        {
            object[] values = { true, 42, 1.5f, Vector2.one, Vector3.one, Quaternion.identity, "\U0001F441\uFE0F",
                new byte[] { 1 }, new bool[] { true }, new int[] { 1 }, new float[] { 1 },
                new[] { Vector2.one }, new[] { Vector3.one }, new[] { Quaternion.identity }, new[] { "hello", null } };
            Check(sender.Encoder.QueueRpcHash(1, RpcHash, values), "Supported argument rejected");
            sender.Encode();
            Check(sender.Payload[20] == values.Length, "Argument count changed");
            sender.Codec.capacity = 64;
            Check(sender.Encoder.QueueRpcHash(1, RpcHash, new byte[40]), "Exact-capacity RPC rejected");
            sender.Encode();
            Check(sender.Payload.Length == 64, "Exact-capacity RPC length changed");
            Check(!sender.Encoder.QueueRpcHash(1, RpcHash, new byte[41]), "One-byte overflow accepted");
            Check(sender.Encoder.QueueRpcHash(1, RpcHash), "No-argument RPC rejected");
            sender.Encode();
        }
    }

    private static void CapacityReduction()
    {
        using (var sender = new Sender())
        using (var receiver = new Receiver())
        {
            var source = new GameObject("Capacity source");
            try
            {
                var probe = source.AddComponent<RpcQueueProbe>();
                probe.networkId = 2;
                sender.Encoder.networkBehaviours = new TSMPNetworkBehaviour[] { probe };
                sender.Encoder.transRpcRepeatFrames = 4;
                Check(sender.Encoder.QueueRpcHash(1, RpcHash, new byte[256]), "Large RPC registration failed");
                sender.Enqueue(77);
                sender.Codec.capacity = 96;
                sender.Codec.fail = true;
                sender.Encoder.EncodeNow();
                Check(sender.Queue.Count == 1 && sender.Queue[0].RepeatsRemaining == 4, "Unsendable head retained or output failure consumed surviving RPC");
                Check(sender.Encoder.frameIndex == 0, "Failed output advanced frame");
                sender.Codec.fail = false;
                for (int i = 0; i < 4; i++)
                {
                    sender.Encode();
                    Check(sender.Encoder.variableMessageCount == 1 && sender.Encoder.rpcMessageCount == 1, "Valid RPC or variables blocked");
                    receiver.Apply(sender.Payload, 1, i != 0);
                }
                Check(sender.Queue.Count == 0, "Repeat budget changed");
                Check(sender.Encoder.QueueRpcHash(1, RpcHash, new byte[40]), "Final head enqueue failed");
                sender.Codec.capacity = 40;
                sender.Encoder.EncodeNow();
                Check(sender.Queue.Count == 0 && sender.Encoder.rpcMessageCount == 0 && sender.Encoder.variableMessageCount == 1,
                    "Discarded head still blocks variable-only frames");
                Check(sender.Encoder.lastError.Contains("RPC discarded:") && sender.Encoder.lastError.Contains("capacity=40"), "Discard diagnosis missing");
            }
            finally { Object.DestroyImmediate(source); }
        }
    }

    private static void MutatedArguments()
    {
        using (var sender = new Sender())
        {
            object[] arguments = { 7 };
            Check(sender.Encoder.QueueRpcHash(1, RpcHash, arguments), "Valid argument rejected");
            sender.Enqueue(77);
            arguments[0] = new object();
            sender.Encoder.debugLog = true;
            bool warned = false;
            Application.LogCallback callback = (message, stack, type) =>
            {
                if (type == LogType.Warning && message.Contains("RPC discarded: networkId=1") && message.Contains("index 0")) warned = true;
            };
            Application.logMessageReceived += callback;
            try { sender.Encoder.EncodeNow(); }
            finally { Application.logMessageReceived -= callback; }
            Check(sender.Encoder.frameIndex == 1 && sender.Queue.Count == 0 && sender.Encoder.rpcMessageCount == 1,
                "Mutated invalid head stopped next RPC");
            Check(sender.Encoder.lastError.Contains("Unsupported RPC argument at index 0"), "Mutation diagnosis missing");
            Check(warned, "Discarded RPC was not logged with its identity and reason");
        }
    }

    private static void CaptureEnqueue()
    {
        foreach (int repeats in new[] { 1, 4 })
        using (var sender = new Sender())
        {
            var source = new GameObject("Capture source");
            try
            {
                var probe = source.AddComponent<RpcQueueProbe>();
                probe.networkId = 1;
                probe.transRpcEncoder = sender.Encoder;
                probe.enqueueOnCapture = true;
                sender.Encoder.networkBehaviours = new TSMPNetworkBehaviour[] { probe };
                sender.Encoder.transRpcRepeatFrames = repeats;
                sender.Encode();
                Check(probe.accepted && sender.Encoder.rpcMessageCount == 0 && sender.Queue[0].RepeatsRemaining == repeats,
                    "Capture-time enqueue consumed before transmission");
                for (int i = 0; i < repeats; i++) sender.Encode();
                Check(sender.Queue.Count == 0, "Capture-time repeat budget changed");
            }
            finally { Object.DestroyImmediate(source); }
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
