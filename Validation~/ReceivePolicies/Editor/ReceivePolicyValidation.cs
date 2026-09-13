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
using UdonSharp;
using UdonSharp.Compiler;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
#endif
using Object = UnityEngine.Object;

public static class ReceivePolicyValidation
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Root = "Assets/Validation/ReceivePolicies/";
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RunCases();
        Finish(false);
    }

    private static void RunCases()
    {
        Test("None ignores scalar and string fields, hash state and callbacks", Scalar);
        string[] fields = { "bytes", "bools", "ints", "floats", "vectors2", "vectors3", "rotations", "strings" };
        for (int i = 0; i < fields.Length; i++)
        {
            int index = i;
            Test("None preserves reused " + fields[i] + " across mode and length changes", () => Arrays(ArrayCacheCases.Types[index], fields[index]));
        }
        Test("A non-network component without receiveInterpolation still receives", Plain);
        Test("None does not suppress outgoing TransSync encoding", SendOnly);
    }

    private sealed class Endpoint : IDisposable
    {
        public readonly Component Target;
#if UDONSHARP
        private readonly IUdonProgram program;
        private readonly IUdonVM vm;
#endif
        public Endpoint(Type type)
        {
#if UDONSHARP
            string path = type == typeof(TSMPEncoder) ? "Packages/com.kibalab.tsmp.core/Runtime/Encoder/TSMPEncoder.asset" : Root + type.Name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            program = asset.SerializedProgramAsset.RetrieveProgram();
            Check(program != null && program.ByteCode.Length > 0, "Missing bytecode: " + path);
            vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            var backing = new GameObject(type.Name).AddComponent<UdonBehaviour>();
            Target = backing;
            Check((bool)typeof(UdonBehaviour).GetMethod("ResolveUdonHeapReferences", Fields).Invoke(backing, new object[] { program.SymbolTable, program.Heap }), "Unresolved heap references");
            typeof(UdonBehaviour).GetField("_program", Fields).SetValue(backing, program);
            typeof(UdonBehaviour).GetField("_udonVM", Fields).SetValue(backing, vm);
            typeof(UdonBehaviour).GetField("_udonManager", Fields).SetValue(backing, UdonManager.Instance);
#else
            Target = new GameObject(type.Name).AddComponent(type);
#endif
        }

        public void Set(string name, object value)
        {
#if UDONSHARP
            uint address = program.SymbolTable.GetAddressFromSymbol(name);
            if (value is ReceiveInterpolationMode mode) value = (int)mode;
            program.Heap.SetHeapVariable(address, value, program.Heap.GetHeapVariableType(address));
#else
            Target.GetType().GetField(name, Fields).SetValue(Target, value);
#endif
        }
        public T Get<T>(string name)
        {
#if UDONSHARP
            return (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
#else
            return (T)Target.GetType().GetField(name, Fields).GetValue(Target);
#endif
        }
        public void Call(string name)
        {
#if UDONSHARP
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name));
            Check(vm.Interpret() == 0, "Udon event failed: " + name);
#else
            Target.GetType().GetMethod(name, Fields).Invoke(Target, null);
#endif
        }
        public void Dispose() => Object.DestroyImmediate(Target.gameObject);
    }

    private static void Apply(Endpoint driver, Endpoint target, string field, int type, object value, int expected)
    {
        var payload = new byte[4096];
        int end = NetworkValueWriter.WriteObject(payload, 0, type, value);
        Check(end >= 0, "Fixture encoding failed");
        Array.Resize(ref payload, end);
        driver.Set("receiver", target.Target);
        driver.Set("fieldName", field);
        driver.Set("valueType", type);
        driver.Set("payload", payload);
        driver.Call("ApplyReceivedValue");
        Check(driver.Get<int>("appliedCount") == expected, "Unexpected applied count");
        Check(driver.Get<int>("rejectedCount") == 0, "Unexpected rejected value type");
    }

    private static void Scalar()
    {
        using (var driver = new Endpoint(typeof(ReceivePolicyProbe)))
        using (var target = new Endpoint(typeof(ReceivePolicyProbe)))
        {
            target.Set("receiveInterpolation", ReceiveInterpolationMode.None);
            target.Set("value", 17);
            target.Set("text", "local");
            target.Set("lastVariableHash", 777u);
            Apply(driver, target, "value", (int)NetworkValueType.Int32, 29, 0);
            Apply(driver, target, "text", (int)NetworkValueType.UTF8String, "remote", 0);
            Check(target.Get<int>("value") == 17 && target.Get<string>("text") == "local", "None overwrote source fields");
            Check(target.Get<uint>("lastVariableHash") == 777 && target.Get<int>("notifications") == 0, "None changed notification state");
            foreach (var mode in new[] { ReceiveInterpolationMode.Discrete, ReceiveInterpolationMode.Continuous })
            {
                target.Set("receiveInterpolation", mode);
                Apply(driver, target, "value", (int)NetworkValueType.Int32, (int)mode, 1);
                Check(target.Get<int>("value") == (int)mode && target.Get<uint>("lastVariableHash") == 100, "Enabled mode did not receive");
            }
            Check(target.Get<int>("notifications") == 2, "Incorrect callback count");
        }
    }

    private static void Arrays(int type, string field)
    {
        using (var driver = new Endpoint(typeof(ReceivePolicyProbe)))
        using (var target = new Endpoint(typeof(ReceivePolicyProbe)))
        {
            Array first = ArrayCacheCases.CreateValue(type, 2, 1);
            Apply(driver, target, field, type, first, 1);
            Array received = target.Get<Array>(field);
            ArrayCacheCases.AssertEqual(first, received);
            foreach (int count in new[] { 2, 3, 0 })
            {
                target.Set("receiveInterpolation", ReceiveInterpolationMode.None);
                target.Set("lastVariableHash", 777u);
                Apply(driver, target, field, type, ArrayCacheCases.CreateValue(type, count, 2), 0);
                Check(ReferenceEquals(received, target.Get<Array>(field)), "None replaced the previous array");
                ArrayCacheCases.AssertEqual(first, received);
                Check(target.Get<uint>("lastVariableHash") == 777 && target.Get<int>("notifications") == 1, "None changed callback state");
            }
            foreach (var mode in new[] { ReceiveInterpolationMode.Continuous, ReceiveInterpolationMode.Discrete })
            {
                target.Set("receiveInterpolation", mode);
                Array next = ArrayCacheCases.CreateValue(type, 2, (int)mode + 5);
                Apply(driver, target, field, type, next, 1);
                Check(ReferenceEquals(received, target.Get<Array>(field)), "Enabled mode lost its reusable array");
                ArrayCacheCases.AssertEqual(next, received);
                target.Set("receiveInterpolation", ReceiveInterpolationMode.None);
                Apply(driver, target, field, type, first, 0);
                ArrayCacheCases.AssertEqual(next, received);
            }
            Check(target.Get<int>("notifications") == 3, "Incorrect resumed callback count");
        }
    }

    private static void Plain()
    {
        using (var driver = new Endpoint(typeof(ReceivePolicyProbe)))
        using (var target = new Endpoint(typeof(PlainReceiveProbe)))
        {
            Apply(driver, target, "value", (int)NetworkValueType.Int32, 42, 1);
            Check(target.Get<int>("value") == 42 && target.Get<uint>("lastVariableHash") == 100 && target.Get<int>("notifications") == 1,
                "Ordinary component binding was blocked");
        }
    }

    private static void SendOnly()
    {
        using (var encoder = new Endpoint(typeof(TSMPEncoder)))
        using (var source = new Endpoint(typeof(ReceivePolicyProbe)))
        {
            var output = new RenderTexture(640, 360, 0, RenderTextureFormat.ARGB32);
            output.Create();
#if !UDONSHARP
            var codec = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/com.kibalab.tsmp.codec.luma4/Runtime/Codec_Luma4.prefab"));
#endif
            try
            {
                source.Set("receiveInterpolation", ReceiveInterpolationMode.None);
                source.Set("networkId", (ushort)1);
                source.Set("value", 42);
                encoder.Set("output", output);
                encoder.Set("autoEncode", false);
                encoder.Set("clearAfterEncode", false);
                encoder.Set("debugLog", false);
#if UDONSHARP
                encoder.Set("bindingUdonTargets", new[] { (UdonBehaviour)source.Target });
                encoder.Set("bindingTargets", new Component[0]);
#else
                encoder.Set("bindingTargets", new[] { source.Target });
                encoder.Set("selectedCodec", codec.GetComponent<TSMPCodec>());
#endif
                encoder.Set("bindingNetworkIds", new ushort[] { 1 });
                encoder.Set("bindingVariableHashes", new uint[] { 100 });
                encoder.Set("bindingFieldNames", new[] { "value" });
                encoder.Set("bindingValueTypes", new[] { (byte)NetworkValueType.Int32 });
                encoder.Set("bindingDirections", new int[1]);
#if UDONSHARP
                encoder.Call("_onEnable");
#endif
                encoder.Call("EncodeNow");
                Check(encoder.Get<uint>("frameIndex") == 1 && encoder.Get<int>("variableMessageCount") == 1,
                    "None suppressed encoding: " + encoder.Get<string>("lastError"));
#if UDONSHARP
                byte[] bytes = encoder.Get<byte[]>("_payloadBytes");
#else
                byte[] bytes = encoder.Get<byte[]>("_encodedPayload");
#endif
                Check(Binary.ReadInt32LE(bytes, NetworkFrameProtocol.NetworkHeaderBytes + 8 + 2 + 7) == 42,
                    "Encoded field value differs");
            }
            finally
            {
#if UDONSHARP
                var texture = encoder.Get<Texture2D>("outputTexture");
                if (texture != null) Object.DestroyImmediate(texture);
#else
                Object.DestroyImmediate(codec);
#endif
                if (RenderTexture.active == output) RenderTexture.active = null;
                output.Release();
                Object.DestroyImmediate(output);
            }
        }
    }

#if UDONSHARP
    public static void RunVm()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach (string name in new[] { nameof(ReceivePolicyProbe), nameof(PlainReceiveProbe) })
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(Root + name + ".asset") != null) continue;
            var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(Root + "Runtime/" + name + ".cs");
            AssetDatabase.CreateAsset(asset, Root + name + ".asset");
        }
        CompileClient();
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.ReceivePolicies.Vm", true);
        Attach();
        EditorApplication.isPlaying = true;
    }

    private static void CompileClient()
    {
        bool error = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = true;
        };
        Application.logMessageReceived += callback;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= callback; }
        Check(!error, "Full Udon client compile failed");
    }

    [InitializeOnLoadMethod]
    private static void Attach()
    {
        if (!SessionState.GetBool("TSMP.ReceivePolicies.Vm", false)) return;
        EditorApplication.playModeStateChanged -= EnteredPlay;
        EditorApplication.playModeStateChanged += EnteredPlay;
    }

    private static void EnteredPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        SessionState.SetBool("TSMP.ReceivePolicies.Vm", false);
        EditorApplication.playModeStateChanged -= EnteredPlay;
        EditorApplication.delayCall += () =>
        {
            Test("Full Udon client compilation in Play Mode", CompileClient);
            if (!failed) RunCases();
            Finish(true);
        };
    }
#endif

    private static void Finish(bool exit)
    {
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), (failed ? "FAIL" : "PASS") +
            "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
        if (exit) EditorApplication.Exit(failed ? 1 : 0);
        else Check(!failed, "Receive policy regressions failed");
    }
    private static void Test(string name, Action action)
    {
        bool loggedError = false;
        Application.LogCallback callback = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) loggedError = true;
        };
        Application.logMessageReceived += callback;
        try { action(); Check(!loggedError, "Test emitted an error log"); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
        finally { Application.logMessageReceived -= callback; }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
