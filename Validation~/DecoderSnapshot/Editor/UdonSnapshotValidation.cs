#if UDONSHARP
using System;
using System.Collections;
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
using Data = DecoderSnapshotValidation;
using Object = UnityEngine.Object;

public static class UdonSnapshotValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator work;
    static readonly List<string> Results = new List<string>();

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        bool failed = false;
        Application.LogCallback listener = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) failed = true;
        };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        Check(!failed, "Full Udon client compilation");
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.SnapshotVm", true);
        Attach();
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    static void Attach()
    {
        EditorApplication.playModeStateChanged -= Entered;
        EditorApplication.playModeStateChanged += Entered;
    }

    static void Entered(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.SnapshotVm", false)) return;
        SessionState.SetBool("TSMP.SnapshotVm", false);
        EditorApplication.delayCall += () =>
        {
            work = Validate();
            EditorApplication.update += Tick;
        };
    }

    static void Tick()
    {
        try { if (work.MoveNext()) return; }
        catch (Exception error) { Finish(error); return; }
        Finish(null);
    }

    static void Finish(Exception error)
    {
        EditorApplication.update -= Tick;
        Results.Insert(0, error == null ? "PASS" : "FAIL");
        if (error != null) Results.Add(error.ToString());
        File.WriteAllLines(Environment.GetEnvironmentVariable("TSMP_SNAPSHOT_RESULTS"), Results);
        EditorApplication.Exit(error == null ? 0 : 1);
    }

    static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    sealed class Program : IDisposable
    {
        public readonly IUdonProgram Code;
        public readonly UdonBehaviour Backing;

        public Program(string path, TSMPCodec codec = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            Code = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(Code);
            Backing = new GameObject(asset.name + " VM").AddComponent<UdonBehaviour>();
            Type type = typeof(UdonBehaviour);
            Check((bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(Backing, new object[] { Code.SymbolTable, Code.Heap }), "Heap references");
            type.GetField("_program", Private).SetValue(Backing, Code);
            type.GetField("_udonVM", Private).SetValue(Backing, vm);
            type.GetField("_udonManager", Private).SetValue(Backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(Backing, true);
            type.GetField("_hasDoneStart", Private).SetValue(Backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(Backing);
            foreach (string name in Code.EntryPoints.GetExportedSymbols())
                events[name] = new List<uint> { Code.EntryPoints.GetAddressFromSymbol(name) };
            if (codec != null)
                foreach (var field in codec.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (Code.SymbolTable.TryGetAddressFromSymbol(field.Name, out uint address))
                        Code.Heap.SetHeapVariable(address, field.GetValue(codec), Code.Heap.GetHeapVariableType(address));
        }

        public void Set<T>(string name, T value) => Code.Heap.SetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
        public T Get<T>(string name) => (T)Code.Heap.GetHeapVariable(Code.SymbolTable.GetAddressFromSymbol(name));
        public void Call(string name)
        {
            Backing.SendCustomEvent(name);
            Check(!(bool)typeof(UdonBehaviour).GetField("_hasError", Private).GetValue(Backing), "VM failure: " + name);
        }
        public void Dispose() => Object.DestroyImmediate(Backing.gameObject);
    }

    static IEnumerator Drain(Program decoder)
    {
        double deadline = EditorApplication.timeSinceStartup + 20;
        while (decoder.Get<bool>("readbackInFlight") && EditorApplication.timeSinceStartup < deadline) yield return null;
        Check(!decoder.Get<bool>("readbackInFlight"), "Actual VRC readback callback timeout");
    }

    static void TestAvatarPool()
    {
        using (var sync = new Program("Packages/com.kibalab.tsmp.core/Runtime/Network/TSMPNetworkVrchatAvatarPoseSync.asset"))
        {
            var avatars = Enumerable.Range(0, 3).Select(i => new GameObject("Pool VM " + i)).ToArray();
            try
            {
                sync.Set("avatarPool", avatars);
                sync.Set("maxPlayers", 3);
                sync.Set("avatarPoseBytes", new byte[] { 4, 2, 0, 0, 0, 0 });
                sync.Call("OnTSMPVariableReceived");
                for (int cycle = 0; cycle < 8; cycle++)
                {
                    sync.Set("_slotPlayerIds", new[] { 1, 2, 3 });
                    sync.Set("_slotLastSeen", new[] { Time.time, Time.time, Time.time });
                    sync.Set("_slotRetireGraceUntil", Time.time + 10);
                    sync.Set("maxPlayers", 1);
                    sync.Call("_postLateUpdate");
                    Check(sync.Get<int>("activeAvatarCount") == 1 && sync.Get<int>("poolSize") == 3, "VM shrink counters");
                    Check(!avatars[1].activeSelf && !avatars[2].activeSelf, "VM overflow deactivation");
                    Check(sync.Get<GameObject[]>("avatarPool").SequenceEqual(avatars), "VM retained objects");
                    sync.Set("maxPlayers", 3);
                    sync.Call("OnTSMPVariableReceived");
                    Check(sync.Get<int[]>("_slotPlayerIds").SequenceEqual(new[] { 1, -1, -1 }), "VM regrowth clears retired IDs");
                    Check(sync.Get<float[]>("_slotLastSeen")[2] == 0, "VM regrowth clears timestamps");
                    Check(sync.Get<int[]>("_recordIndexSlots").All(i => i == -1), "VM invalidates slot cache");
                    sync.Set("avatarPoseBytes", new byte[] { 4, 2, 3, 0, 0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0, 0, 0, 0 });
                    sync.Call("OnTSMPVariableReceived");
                    Check(sync.Get<int>("activeAvatarCount") == 3 && sync.Get<GameObject[]>("avatarPool").SequenceEqual(avatars), "VM slot reuse");
                    sync.Set("avatarPoseBytes", new byte[] { 4, 2, 0, 0, 0, 0 });
                }
                Results.Add("PASS avatar pool VM: eight shrink/regrow cycles, idle resize, inactive retained objects, IDs, timestamps, cache and slot reuse (pool-only fixtures without rigs)");
            }
            finally { foreach (var avatar in avatars) Object.DestroyImmediate(avatar); }
        }
    }

    static IEnumerator Validate()
    {
        TestAvatarPool();
        Results.Add("Full client UdonSharp compile; actual decoder/codec bytecode, VRCGraphics and asynchronous VRC GPU readback callbacks in editor VM");
        Results.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName);
        var names = new[] { "Luma4", "RGB16", "RGB20", "Color256" };
        var owned = new List<Material>();
        var codecs = names.Select(name => Data.InstantiateCodec(AssetDatabase.LoadAssetAtPath<GameObject>(
            "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab").GetComponent<TSMPCodec>(), owned)).ToArray();
        var programs = names.Select((name, i) => new Program("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() +
            "/Runtime/Scripts/TSMPCodec" + name + ".asset", codecs[i])).ToArray();
        using (var decoder = new Program("Packages/com.kibalab.tsmp.core/Runtime/Decoder/TSMPDecoder.asset"))
        {
            var a = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var b = new Texture2D(640, 360, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            var source = Data.Target(640, 360);
            var output = Data.Target(512, 1);
            decoder.Set<Texture>("sourceTexture", source);
            decoder.Set("payloadByteTexture", output);
            decoder.Set("codecHandlers", programs.Select(p => p.Backing).ToArray());
            decoder.Set("applyEveryFrame", false);
            decoder.Set("skipDuplicateFrames", false);
            decoder.Set("decodeSafetyMode", 2);
            decoder.Set("debugLog", false);
            decoder.Call("_onEnable");
            uint frame = 0;
            foreach (var codec in codecs)
            foreach (int sample in new[] { 1, 4 })
            foreach (bool changeCodec in new[] { false, true })
            {
                byte[] bytes = Data.Payload((int)++frame);
                Data.Write(codec, a, bytes, frame, sample);
                Data.Write(changeCodec ? codecs[(Array.IndexOf(codecs, codec) + 1) % codecs.Length] : codec,
                    b, Data.Payload(193, changeCodec ? 67 : bytes.Length), frame + 10000, sample);
                Graphics.Blit(a, source);
                decoder.Set("sampleSize", sample);
                decoder.Call("DecodeNow");
                Check(decoder.Get<bool>("readbackInFlight"), "VM header readback not started: " + decoder.Get<string>("lastError"));
                Graphics.Blit(b, source);
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(decoder.Get<bool>("lastFrameValid"), decoder.Get<string>("lastError"));
                Check(decoder.Get<uint>("lastFrameIndex") == frame && decoder.Get<int>("_payloadDataBytes") == bytes.Length && bytes.SequenceEqual(decoder.Get<byte[]>("_payloadBytes").Take(bytes.Length)), "VM frame isolation " + codec.displayName);
            }
            Results.Add("PASS " + frame + " decoder/codec VM changing-source cases, samples 1/4, payload/codec/length changes");
            decoder.Set("decodeSafetyMode", 3);
            decoder.Set("sampleSize", 1);
            foreach (int length in new[] { 200, 0, 400, 0, 1, 400, 0 })
            {
                byte[] bytes = Data.NetworkPayload(length);
                byte[] previous = decoder.Get<byte[]>("_payloadBytes");
                Data.Write(codecs[0], a, bytes, ++frame, 1);
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(decoder.Get<bool>("lastFrameValid"), "VM capacity frame: " + decoder.Get<string>("lastError"));
                Check(decoder.Get<int>("lastNetworkMessageCount") == 1 && decoder.Get<int>("lastPayloadAvailableBytes") == bytes.Length, "VM parsing bounds");
                Check(bytes.SequenceEqual(decoder.Get<byte[]>("_payloadBytes").Take(bytes.Length)), "VM payload bytes");
                if (previous.Length >= bytes.Length) Check(ReferenceEquals(previous, decoder.Get<byte[]>("_payloadBytes")), "VM capacity replacement");
            }
            foreach (int length in new[] { 0, 7 })
            {
                Data.Write(codecs[0], a, new byte[length], ++frame, 1);
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(!decoder.Get<bool>("lastFrameValid") && decoder.Get<int>("_decodeStage") == 1 && decoder.Get<string>("lastError") == "Payload is too small for NetworkFrame.", "VM invalid length must not reuse old payload");
            }
            Results.Add("PASS GPU VM payload capacity: seven growing/shrinking NetworkFrames, retained dirty tails, zero/truncated header payload rejection");
            decoder.Set("decodeSafetyMode", 2);
            Data.Write(codecs[0], a, Data.Payload(77), ++frame, 1);
            foreach (bool payload in new[] { false, true })
            foreach (bool objectDisable in new[] { false, true })
            foreach (bool earlyEnable in new[] { false, true })
            {
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                if (payload)
                {
                    while (decoder.Get<bool>("readbackInFlight") && decoder.Get<int>("_decodeStage") == 1) yield return null;
                    Check(decoder.Get<bool>("readbackInFlight"), "VM payload cancellation window");
                }
                var snapshot = decoder.Get<RenderTexture>("_decodeSourceTexture");
                if (objectDisable) decoder.Backing.gameObject.SetActive(false); else decoder.Backing.enabled = false;
                Check(decoder.Get<bool>("_decodeSuspended"), "Actual backing disable event not delivered");
                if (earlyEnable)
                {
                    if (objectDisable) decoder.Backing.gameObject.SetActive(true); else decoder.Backing.enabled = true;
                }
                decoder.Call("DecodeNow");
                Check(decoder.Get<RenderTexture>("_decodeSourceTexture") == null, "VM reused snapshot while cancelled callback pending");
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(!decoder.Get<bool>("lastFrameValid"), "VM cancelled frame applied");
                if (objectDisable) decoder.Backing.gameObject.SetActive(true); else decoder.Backing.enabled = true;
                yield return null;
                Check(snapshot == null, "VM snapshot leaked");
                decoder.Call("DecodeNow");
                drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                Check(decoder.Get<bool>("lastFrameValid"), "VM restart failed");
            }
            Results.Add("PASS 8 VM cancellation cases: actual component/GameObject disable during header/payload, callbacks while disabled or after early re-enable, restart and snapshot cleanup");
            decoder.Set("decodeSafetyMode", 0);
            Check(decoder.Get<int>("frameWindowSize") == 256, "VM default frame window");
            var empty = new byte[NetworkFrameProtocol.NetworkHeaderBytes];
            NetworkFrameWriter.BeginNetworkFrame(empty, 0, 1);
            foreach (var item in Data.FrameOrderCases())
            {
                decoder.Set("_hasAppliedFrame", item[6] != 0);
                decoder.Set("_lastAppliedStreamId", item[4]);
                decoder.Set("_lastAppliedFrameIndex", item[0]);
                decoder.Set("frameWindowSize", (int)item[2]);
                decoder.Set("skipDuplicateFrames", item[5] == 0);
                int duplicates = decoder.Get<int>("skippedDuplicateFrameCount");
                int older = decoder.Get<int>("skippedOutOfOrderFrameCount");
                Data.Write(codecs[0], a, empty, item[1], 4);
                Graphics.Blit(a, source);
                decoder.Call("DecodeNow");
                var drain = Drain(decoder);
                while (drain.MoveNext()) yield return drain.Current;
                bool accept = item[3] != 0;
                Check(decoder.Get<bool>("lastFrameValid"), "VM window frame validity");
                Check(decoder.Get<uint>("_lastAppliedFrameIndex") == (accept ? item[1] : item[0]), "VM window " + string.Join(",", item));
                Check(decoder.Get<int>("skippedDuplicateFrameCount") == duplicates + (!accept && item[0] == item[1] ? 1 : 0), "VM duplicate counter");
                Check(decoder.Get<int>("skippedOutOfOrderFrameCount") == older + (!accept && item[0] != item[1] ? 1 : 0), "VM older counter");
            }
            Results.Add("PASS 30 compiled Udon frame-window cases including UInt32 wrap, half-range, custom windows, stream switch and disabled filtering");
            decoder.Call("_onDisable");
            RenderTexture.active = null;
            source.Release();
            output.Release();
            foreach (Object item in new Object[] { a, b, source, output }) Object.DestroyImmediate(item);
        }
        foreach (var program in programs) { program.Call("_onDisable"); program.Dispose(); }
        foreach (var codec in codecs) Object.DestroyImmediate(codec.gameObject);
        foreach (var material in owned) Object.DestroyImmediate(material);
    }
}
#endif
