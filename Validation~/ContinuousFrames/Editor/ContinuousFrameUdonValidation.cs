#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

public static class ContinuousFrameUdonValidation
{
    const string ProbePath = "Assets/ContinuousFrames/ContinuousFrameProbe.asset";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        ContinuousFrameValidation.CreateScene();
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ProbePath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/ContinuousFrames/ContinuousFrameProbe.cs");
            AssetDatabase.CreateAsset(asset, ProbePath);
        }
        bool failed = false;
        Application.LogCallback listener = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) failed = true;
        };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        if (failed) throw new InvalidOperationException("Full client UdonSharp compilation failed");
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.ContinuousUdon", true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        if (SessionState.GetBool("TSMP.ContinuousUdon", false))
            ContinuousFrameValidation.UdonFactory = (runner, test) => new Loopback(runner, test);
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.ContinuousUdon", false)) return;
        SessionState.SetBool("TSMP.ContinuousUdon", false);
        ContinuousFrameValidation.UdonFactory = (runner, test) => new Loopback(runner, test);
    }

    sealed class Program : IDisposable
    {
        readonly IUdonProgram code;
        public readonly UdonBehaviour Backing;

        public Program(string path, TSMPCodec codec = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            code = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(code);
            Backing = new GameObject(asset.name + " continuous VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            if (!(bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { code.SymbolTable, code.Heap }))
                throw new InvalidOperationException("Unresolved Udon heap");
            type.GetField("_program", Private).SetValue(Backing, code);
            type.GetField("_udonVM", Private).SetValue(Backing, vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(Backing, true);
            type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in code.EntryPoints.GetExportedSymbols())
                events[name] = new List<uint> { code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec == null) return;
            foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address))
                    code.Heap.SetHeapVariable(address, field.GetValue(codec), code.Heap.GetHeapVariableType(address));
        }

        public void Set<T>(string name, T value) => code.Heap.SetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)code.Heap.GetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Backing.SendCustomEvent(name);
            if ((bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing))
                throw new InvalidOperationException("Udon VM failed: " + name);
        }
        public void Dispose() => Object.DestroyImmediate(Backing.gameObject);
    }

    sealed class Loopback : ContinuousFrameValidation.ILoopback
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<Program> programs = new List<Program>();
        readonly Program encoder;
        readonly Program decoder;
        readonly Program sender;
        readonly Program receiver;

        public Loopback(ContinuousFrameValidation runner, ContinuousFrameValidation.Case test)
        {
            string[] names = { "Luma4", "RGB16", "RGB20", "Color256" };
            TSMPCodec native = runner.CloneCodec(test.Codec, owned);
            var codec = Add("Packages/com.kibalab.tsmp.codec." + names[test.Codec].ToLowerInvariant() + "/Runtime/Scripts/TSMPCodec" + names[test.Codec] + ".asset", native);
            Program luma = test.Codec == 0 ? codec : Add("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Scripts/TSMPCodecLuma4.asset", runner.CloneCodec(0, owned));
            sender = Add(ProbePath);
            receiver = Add(ProbePath);
            sender.Set("networkId", (ushort)1);
            receiver.Set("networkId", (ushort)1);
            receiver.Set("appliedIds", new int[ContinuousFrameValidation.Capacity]);
            receiver.Set("appliedTimes", new float[ContinuousFrameValidation.Capacity]);

            var source = ContinuousFrameValidation.Texture(test.Width, test.Height);
            var bytes = ContinuousFrameValidation.Texture(512, Math.Max(1, (test.Bytes + 64 + 2047) / 2048));
            owned.Add(source);
            owned.Add(bytes);
            var expand = new Material(runner.expandMaterial);
            owned.Add(expand);
            encoder = Add("Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset");
            encoder.Set("output", source);
            encoder.Set("autoEncode", false);
            encoder.Set("sampleSize", test.Sample);
            encoder.Set("frameIndex", 1u);
            encoder.Set("blockExpandMaterial", expand);
            encoder.Set("selectedCodecUdonTarget", codec.Backing);
            encoder.Set("debugLog", false);
            Bind(encoder, sender);
            encoder.Set("bindingSendOnChange", new[] { false });
            encoder.Set("bindingMinSendIntervals", new[] { 0f });
            encoder.Call("_onEnable");

            decoder = Add("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset");
            decoder.Set<Texture>("sourceTexture", source);
            decoder.Set("payloadByteTexture", bytes);
            decoder.Set("codecHandlers", test.Codec == 0 ? new[] { luma.Backing } : new[] { luma.Backing, codec.Backing });
            decoder.Set("applyEveryFrame", false);
            decoder.Set("flipY", true);
            decoder.Set("sampleSize", test.Sample);
            decoder.Set("debugLog", true);
            Bind(decoder, receiver);
            decoder.Call("_onEnable");
        }

        Program Add(string path, TSMPCodec codec = null)
        {
            var program = new Program(path, codec);
            programs.Add(program);
            return program;
        }
        static void Bind(Program owner, Program target)
        {
            var field = TransSyncMetadata.GetOrCreate(null, typeof(ContinuousFrameProbe)).Fields.Single();
            owner.Set("bindingTargets", new Component[0]);
            owner.Set("bindingUdonTargets", new[] { target.Backing });
            owner.Set("bindingNetworkIds", new ushort[] { 1 });
            owner.Set("bindingVariableHashes", new[] { field.VariableHash });
            owner.Set("bindingValueTypes", new[] { (byte)field.ValueType });
            owner.Set("bindingFieldNames", new[] { nameof(ContinuousFrameProbe.packet) });
            owner.Set("bindingDirections", new[] { 0 });
            owner.Set("bindingPriorities", new[] { 0 });
        }
        public bool Busy => decoder.Get<bool>("readbackInFlight");
        public int PayloadBytes => Binary.ReadUInt16LE(encoder.Get<byte[]>("_headerBytes"), FrameHeader.PayloadSizeOffset);
        public int AppliedCount => receiver.Get<int>("appliedCount");
        public int CorruptCount => receiver.Get<int>("corruptCount");
        public int[] AppliedIds => receiver.Get<int[]>("appliedIds");
        public float[] AppliedTimes => receiver.Get<float[]>("appliedTimes");
        public string Error => encoder.Get<string>("lastError");
        public string DecoderError => decoder.Get<bool>("lastFrameValid") ? null : decoder.Get<string>("lastError");
        public string Diagnostics => "header=" + decoder.Get<bool>("lastHeaderValid") + ", valid=" + decoder.Get<bool>("lastFrameValid") + ", messages=" + decoder.Get<int>("lastNetworkMessageCount") + ", applied=" + decoder.Get<int>("lastAppliedVariableCount") + ", error=" + decoder.Get<string>("lastError");
        public void Publish(byte[] packet)
        {
            sender.Set("packet", packet);
            uint before = encoder.Get<uint>("frameIndex");
            encoder.Call("EncodeNow");
            if (encoder.Get<uint>("frameIndex") != before + 1)
                throw new InvalidOperationException("Encoder output failure: " + Error);
        }
        public void Decode() => decoder.Call("DecodeNow");
        public void Stop() => decoder.Call("_onDisable");
        public void Dispose()
        {
            RenderTexture.active = null;
            foreach (var program in programs) program.Dispose();
            foreach (var item in owned)
            {
                if (item is RenderTexture texture) texture.Release();
                if (item != null) Object.Destroy(item);
            }
        }
    }
}
#endif
