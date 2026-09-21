#if UDONSHARP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;

public static class RpcQueueVmValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string ProbeAsset = "Assets/Validation/RpcDelivery/RpcQueueProbe.asset";
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbeAsset);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Validation/RpcDelivery/Runtime/RpcQueueProbe.cs");
            AssetDatabase.CreateAsset(asset, ProbeAsset);
        }
        CompileClient();
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.RpcQueue.Vm", true);
        Attach();
        EditorApplication.isPlaying = true;
    }

    private static void CompileClient()
    {
        bool compileError = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) compileError = true;
        };
        Application.logMessageReceived += callback;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= callback; }
        Check(!compileError, "Full Udon client compilation failed");
    }

    [InitializeOnLoadMethod]
    private static void Attach()
    {
        if (!SessionState.GetBool("TSMP.RpcQueue.Vm", false)) return;
        EditorApplication.playModeStateChanged -= EnteredPlay;
        EditorApplication.playModeStateChanged += EnteredPlay;
    }

    private static void EnteredPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetBool("TSMP.RpcQueue.Vm", false);
        EditorApplication.playModeStateChanged -= EnteredPlay;
        EditorApplication.delayCall += () =>
        {
            Test("Full Udon client compilation after entering Play Mode", CompileClient);
            if (!failed)
            {
                foreach (int repeats in new[] { 1, 4 })
                foreach (bool existing in new[] { false, true })
                foreach (bool failOutput in new[] { false, true })
                    Test("repeats=" + repeats + ", existing=" + existing + ", failed output=" + failOutput,
                        () => Verify(repeats, existing, failOutput));
                Test("Manual RPC does not consume a capture-time queued event", ManualRpc);
            }
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"),
                (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion +
                "\nExecution=Udon client bytecode in editor VM\nGraphics=" + SystemInfo.graphicsDeviceName +
                " / " + SystemInfo.graphicsDeviceType + "\n" + string.Join("\n", Results));
            EditorApplication.Exit(failed ? 1 : 0);
        };
    }

    private sealed class VmProgram : IDisposable
    {
        public readonly IUdonProgram Program;
        public readonly IUdonVM Vm;
        public readonly UdonBehaviour Backing;

        public VmProgram(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            Program = asset.SerializedProgramAsset.RetrieveProgram();
            Check(Program != null && Program.ByteCode.Length > 0, "Missing client bytecode: " + path);
            Vm = UdonEditorManager.Instance.ConstructUdonVM();
            Vm.LoadProgram(Program);
            Backing = new GameObject(asset.name + " VM").AddComponent<UdonBehaviour>();
            var type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Program.SymbolTable, Program.Heap }), "Unresolved heap references");
            type.GetField("_program", Private).SetValue(Backing, Program);
            type.GetField("_udonVM", Private).SetValue(Backing, Vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
        }

        public void Set<T>(string name, T value) => Program.Heap.SetHeapVariable(Program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Program.Heap.GetHeapVariable(Program.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Vm.SetProgramCounter(Program.EntryPoints.GetAddressFromSymbol(name));
            Check(Vm.Interpret() == 0, "Udon VM event failed: " + name);
        }
        public void Dispose() => UnityEngine.Object.DestroyImmediate(Backing.gameObject);
    }

    private sealed class Sender : IDisposable
    {
        public readonly VmProgram Encoder = new VmProgram("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset");
        public readonly VmProgram Source = new VmProgram(ProbeAsset);
        private readonly RenderTexture output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);

        public Sender(int repeats)
        {
            output.Create();
            Encoder.Set("output", output);
            Encoder.Set("autoEncode", false);
            Encoder.Set("clearAfterEncode", false);
            Encoder.Set("debugLog", false);
            Encoder.Set("transRpcRepeatFrames", repeats);
            Encoder.Set("bindingTargets", new Component[0]);
            Encoder.Set("bindingUdonTargets", new[] { Source.Backing });
            Encoder.Set("bindingNetworkIds", new ushort[] { 1 });
            Encoder.Set("bindingVariableHashes", new[] { StableHash.Fnv1A32("value") });
            Encoder.Set("bindingValueTypes", new[] { (byte)NetworkValueType.Int32 });
            Encoder.Set("bindingFieldNames", new[] { "value" });
            Encoder.Set("bindingDirections", new int[1]);
            Encoder.Set("bindingSendOnChange", new[] { false });
            Source.Set("transRpcEncoder", Encoder.Backing);
            Source.Set("networkId", (ushort)1);
            Encoder.Call("_onEnable");
        }

        public List<int> Encode(bool success = true, bool clear = true)
        {
            if (clear) Encoder.Call("ClearFrame");
            uint before = Encoder.Get<uint>("frameIndex");
            Encoder.Call("EncodeNow");
            Check(Encoder.Get<uint>("frameIndex") == before + (success ? 1u : 0u), "Unexpected frame advancement: " + Encoder.Get<string>("lastError"));
            if (!success)
            {
                Check(!string.IsNullOrEmpty(Encoder.Get<string>("lastError")), "Failure diagnostic missing");
                return new List<int>();
            }
            Check(string.IsNullOrEmpty(Encoder.Get<string>("lastError")), Encoder.Get<string>("lastError"));
            Check(Encoder.Get<int>("variableMessageCount") == 1, "Variable message missing");
            byte[] bytes = Encoder.Get<byte[]>("_payloadBytes");
            int length = Encoder.Get<int>("payloadBytes");
            Check(FrameHeader.TryRead(Encoder.Get<byte[]>("_headerBytes"), 0, out FrameHeader header) && header.PayloadSize == length,
                "Encoded header failed CRC/length validation");
            var events = new List<int>();
            int cursor = NetworkFrameProtocol.NetworkHeaderBytes;
            int messages = Binary.ReadUInt16LE(bytes, 2);
            for (int i = 0; i < messages; i++)
            {
                int end = cursor + 8 + Binary.ReadUInt16LE(bytes, cursor + 6);
                Check(end <= length, "RPC frame malformed");
                if (bytes[cursor + 2] == NetworkFrameProtocol.MessageTypeRpcCall && bytes[cursor + 12] == 2)
                {
                    int argument = cursor + 13;
                    argument += 3 + Binary.ReadUInt16LE(bytes, argument + 1);
                    events.Add(Binary.ReadInt32LE(bytes, argument + 3));
                }
                cursor = end;
            }
            Check(cursor == length, "Frame tail mismatch");
            return events;
        }

        public void Dispose()
        {
            var texture = Encoder.Get<Texture2D>("outputTexture");
            Encoder.Dispose();
            Source.Dispose();
            if (RenderTexture.active == output) RenderTexture.active = null;
            output.Release();
            UnityEngine.Object.DestroyImmediate(output);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void Verify(int repeats, bool existing, bool failOutput)
    {
        using (var sender = new Sender(repeats))
        {
            if (existing) sender.Source.Call("QueueEvent");
            sender.Source.Set("enqueueOnCapture", true);
            sender.Source.Set("failFrame", failOutput);
            var events = sender.Encode(!failOutput);
            Check(sender.Source.Get<bool>("accepted"), "Capture enqueue failed");
            int expectedCount = existing && (failOutput || repeats > 1) ? 2 : 1;
            Check(sender.Encoder.Get<int>("_pendingRpcCount") == expectedCount, "Unsent queued event was removed");
            Check(sender.Encoder.Get<int[]>("_pendingRpcRepeatsRemaining")[expectedCount - 1] == repeats,
                "Capture-time event consumed a repeat before transmission");
            sender.Source.Set("failFrame", false);
            int attempts = 0;
            while (sender.Encoder.Get<int>("_pendingRpcCount") > 0 && attempts++ < 16)
                events.AddRange(sender.Encode());
            Check(sender.Encoder.Get<int>("_pendingRpcCount") == 0, "Queue never exhausted");
            var expected = Enumerable.Repeat(1, repeats);
            if (existing) expected = expected.Concat(Enumerable.Repeat(2, repeats));
            Check(events.SequenceEqual(expected), "Wrong on-wire events: " + string.Join(",", events));
            Check(sender.Encode().Count == 0, "Exhausted event was retransmitted");
            Check(sender.Encoder.Get<Texture2D>("outputTexture").GetPixels32().Any(p => p.r > 0), "Luma4 texture is blank");
        }
    }

    private static void ManualRpc()
    {
        using (var sender = new Sender(1))
        {
            sender.Source.Call("WriteManualRpc");
            sender.Source.Set("enqueueOnCapture", true);
            Check(sender.Encode(clear: false).Count == 0 && sender.Encoder.Get<int>("rpcMessageCount") == 1, "Manual RPC fixture invalid");
            Check(sender.Encoder.Get<int>("_pendingRpcCount") == 1, "Manual RPC consumed an unrelated queued event");
            Check(sender.Encode().SequenceEqual(new[] { 1 }), "Capture-time event was not transmitted next frame");
        }
    }

    private static void Test(string name, Action action)
    {
        try { action(); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
