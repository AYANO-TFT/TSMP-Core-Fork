#if UNITY_EDITOR && UDONSHARP && !COMPILER_UDONSHARP
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using Object = UnityEngine.Object;

public static class BulkCopyValidation
{
    const string AssetPath = "Assets/OptimizationFeasibility/BulkCopyProbe.asset";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IUdonProgram program;
    static IUdonVM vm;
    static UdonBehaviour backing;
    static Texture2D texture;
    static int readbackCase;
    static double deadline;
    static readonly List<string> lines = new List<string>();
    static readonly List<string> timings = new List<string>();
    static readonly int[] ByteCounts = { 32, 4096, 65535 };

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/OptimizationFeasibility/BulkCopyProbe.cs");
            AssetDatabase.CreateAsset(asset, AssetPath);
        }
        bool failed = false;
        Application.LogCallback listener = (text, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception) failed = true;
        };
        Application.logMessageReceived += listener;
        try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
        finally { Application.logMessageReceived -= listener; }
        if (failed) throw new InvalidOperationException("Udon client compilation failed");
        AssetDatabase.SaveAssets();
        SessionState.SetBool("TSMP.BulkCopy", true);
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
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.BulkCopy", false)) return;
        SessionState.SetBool("TSMP.BulkCopy", false);
        try
        {
            program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(AssetPath).SerializedProgramAsset.RetrieveProgram();
            vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            backing = new GameObject("Bulk copy VM").AddComponent<UdonBehaviour>();
            var type = typeof(UdonBehaviour);
            if (!(bool)type.GetMethod("ResolveUdonHeapReferences", Private).Invoke(backing, new object[] { program.SymbolTable, program.Heap }))
                throw new InvalidOperationException("Unresolved heap");
            type.GetField("_program", Private).SetValue(backing, program);
            type.GetField("_udonVM", Private).SetValue(backing, vm);
            type.GetField("_udonManager", Private).SetValue(backing, UdonManager.Instance);
            type.GetField("_isReady", Private).SetValue(backing, true);
            type.GetField("_hasDoneStart", Private).SetValue(backing, true);
            var events = (Dictionary<string, List<uint>>)type.GetField("_eventTable", Private).GetValue(backing);
            foreach (string name in program.EntryPoints.GetExportedSymbols())
                events[name] = new List<uint> { program.EntryPoints.GetAddressFromSymbol(name) };
            lines.Add("Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType);
            lines.Add("Mode=Client-target Udon bytecode in SDK Editor VM, not VRChat client");
            timings.Add("case,count,event,mean_us,p95_us,samples");
            Benchmark();
            BenchmarkRaster();
            BeginReadback();
            EditorApplication.update += Poll;
        }
        catch (Exception error) { Finish(error); }
    }

    static void Set<T>(string name, T value) => program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
    static T Get<T>(string name) => (T)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name));
    static void Require(bool valid, string text) { if (!valid) throw new InvalidOperationException(text); }
    static void Call(string name)
    {
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name));
        Require(vm.Interpret() == 0, "VM failure: " + name);
    }

    static void Time(string label, int count, string name)
    {
        uint address = program.EntryPoints.GetAddressFromSymbol(name);
        double[] values = new double[80];
        for (int i = -10; i < values.Length; i++)
        {
            vm.SetProgramCounter(address);
            long start = Stopwatch.GetTimestamp();
            uint status = vm.Interpret();
            long end = Stopwatch.GetTimestamp();
            Require(status == 0, name);
            if (i >= 0) values[i] = (end - start) * 1000000.0 / Stopwatch.Frequency;
        }
        Array.Sort(values);
        timings.Add(string.Join(",", label, count, name, values.Average().ToString("F4", CultureInfo.InvariantCulture),
            values[(int)(values.Length * 0.95)].ToString("F4", CultureInfo.InvariantCulture), values.Length));
    }

    static void Benchmark()
    {
        Time("control", 0, "Control");
        foreach (int count in new[] { 3600, 14400, 32400, 129600 })
        {
            var source = Enumerable.Range(0, count).Select(i => new Color32((byte)i, (byte)(i >> 8), (byte)(i * 73), 255)).ToArray();
            var destination = new Color32[count];
            Set("sourceColors", source); Set("targetColors", destination); Set("count", count);
            Time("colors", count, "LoopColors");
            Require(source.SequenceEqual(destination), "Loop colors mismatch");
            Array.Clear(destination, 0, destination.Length);
            Time("colors", count, "BulkColors");
            Require(source.SequenceEqual(destination), "Bulk colors mismatch");
        }
        foreach (int count in ByteCounts)
        {
            var source = Enumerable.Range(0, count).Select(i => (byte)(i * 73)).ToArray();
            var destination = new byte[count + 113];
            Set("sourceBytes", source); Set("targetBytes", destination);
            Set("sourceOffset", 0); Set("targetOffset", 56); Set("count", count);
            Time("bytes", count, "LoopBytes");
            Require(source.SequenceEqual(destination.Skip(56).Take(count)), "Loop bytes mismatch");
            Array.Clear(destination, 0, destination.Length);
            Time("bytes", count, "BulkBytes");
            Require(source.SequenceEqual(destination.Skip(56).Take(count)) && destination.Take(56).All(v => v == 0) &&
                destination.Skip(56 + count).All(v => v == 0), "Bulk bytes or sentinel mismatch");
        }
        foreach (int length in new[] { 0, 1, 31, 4096 })
        foreach (bool reverse in new[] { false, true })
        {
            byte[] actual = Enumerable.Range(0, length + 16).Select(i => (byte)i).ToArray();
            byte[] expected = (byte[])actual.Clone();
            int from = reverse ? 7 : 2;
            int to = reverse ? 2 : 7;
            Array.Copy(expected, from, expected, to, length);
            Set("sourceBytes", actual); Set("targetBytes", actual);
            Set("sourceOffset", from); Set("targetOffset", to); Set("count", length);
            Call("BulkBytes");
            Require(actual.SequenceEqual(expected), "Overlap mismatch");
        }
        lines.Add("PASS byte/Color32 bulk copy, destination isolation, offset sentinels, zero count and both overlap directions");
        byte[] buffer = { 7, 8, 9 };
        Set("sourceBytes", (byte[])null); Set("targetBytes", buffer); Set("targetOffset", 3);
        Call("LoopBytes");
        Require(Get<int>("writeResult") == 3 && buffer.SequenceEqual(new byte[] { 7, 8, 9 }), "Null write semantics");
        Set("sourceBytes", new byte[] { 1, 2 }); Set("targetOffset", 2);
        Call("LoopBytes");
        Require(Get<int>("writeResult") == -1 && buffer.SequenceEqual(new byte[] { 7, 8, 9 }), "Invalid write mutation");
        Set("sourceBytes", buffer); Set("sourceOffset", 1); Set("count", 2); Set("targetBytes", (byte[])null);
        Call("ReadBytes");
        byte[] cache = Get<byte[]>("targetBytes");
        Require(cache.SequenceEqual(new byte[] { 8, 9 }) && !ReferenceEquals(cache, buffer), "Receiver ownership");
        cache[0] = 55;
        Require(buffer[1] == 8, "Receiver mutation reached source");
        Call("ReadBytes");
        Require(ReferenceEquals(cache, Get<byte[]>("targetBytes")) && cache[0] == 8, "Receiver cache reuse");
        Set("count", 0); Set("sourceBytes", (byte[])null);
        Call("ReadBytes");
        Require(Get<byte[]>("targetBytes").Length == 0, "Empty reader");
        Set("sourceColors", new[] { new Color32(1, 2, 3, 4) });
        var colors = new[] { new Color32(), new Color32(9, 8, 7, 6) };
        Set("targetColors", colors); Call("LoopColors");
        Require(colors[0].r == 1 && colors[1].r == 9, "Minimum color length");
        lines.Add("PASS production null/empty writes, invalid-range rejection, receiver ownership/reuse/resize and unequal color-buffer lengths");
    }

    static void BenchmarkRaster()
    {
        var material=new Material(Resources.Load<Material>("TSMPEncodeLuma4"));
        var expand=new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat"));
        Set("rasterMaterial",material); Set("expandMaterial",expand);
        Set("rasterHeader",Enumerable.Range(0,56).Select(i=>(byte)(i*71)).ToArray());
        Set("lumaColors",SymbolCodec.CreateLuma4Colors());
        foreach(var size in new[] { new Vector2Int(640,360),new Vector2Int(1280,720),new Vector2Int(3840,2160) })
        {
            var output=new RenderTexture(size.x,size.y,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            output.Create();
            var symbols=new Texture2D(size.x/8,size.y/8,TextureFormat.RGBA32,false,false) { filterMode=FilterMode.Point };
            var pixels=new Color32[size.x/8*(size.y/8)];
            EncoderUdonTextureRuntime.ClearPixelBuffer(pixels);
            Luma4FrameTextureWriter.WriteStaticRegions(pixels,size.x,size.y,8,size.x/8,size.y/8,true);
            Set("rasterOutput",output); Set("rasterTexture",symbols); Set("rasterBase",pixels); Set("rasterPixels",new Color32[pixels.Length]);
            int capacity=Luma4Raster.GetPayloadCapacityBytes(size.x,size.y,8);
            foreach(int count in new[] { 0,32,256,Math.Min(4096,capacity),capacity,32 })
            {
                Set("sourceBytes",Enumerable.Range(0,count).Select(i=>(byte)(i*73)).ToArray()); Set("count",count);
                Time("raster-"+size.x+"x"+size.y,count,"CpuRaster");
                var request=UnityEngine.Rendering.AsyncGPUReadback.Request(output,0);
                request.WaitForCompletion(); Require(!request.hasError,"CPU raster readback");
                var expected=request.GetData<Color32>().ToArray();
                Time("raster-"+size.x+"x"+size.y,count,"GpuRaster");
                Require(Get<bool>("rasterResult"),"GPU raster failed");
                request=UnityEngine.Rendering.AsyncGPUReadback.Request(output,0);
                request.WaitForCompletion(); Require(!request.hasError,"GPU raster readback");
                Require(expected.SequenceEqual(request.GetData<Color32>().ToArray()),"Udon CPU/GPU raster differs: "+size+" bytes="+count);
            }
            output.Release(); Object.Destroy(output); Object.Destroy(symbols);
        }
        Call("ReleaseRaster");
        Object.Destroy(material); Object.Destroy(expand);
        lines.Add("PASS Udon CPU/GPU full-image equality: 360p/720p/4K, empty/small/large/capacity/shrinking payloads");
    }

    static void BeginReadback()
    {
        int payloadCount = ByteCounts[readbackCase];
        int height = (56 + payloadCount + 1023) / 1024;
        texture = new Texture2D(256, height, TextureFormat.RGBA32, false, true);
        var pixels = Enumerable.Range(0, 256 * height).Select(i => new Color32((byte)i, (byte)(i * 37 + 3), (byte)(i * 73 + 9), (byte)(i * 91 + 11))).ToArray();
        byte[] packed = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            packed[i * 4] = pixels[i].r; packed[i * 4 + 1] = pixels[i].g;
            packed[i * 4 + 2] = pixels[i].b; packed[i * 4 + 3] = pixels[i].a;
        }
        Set("sourceBytes", packed); Set("uploadTexture", texture);
        Call("UploadBytes");
        Set("sourceTexture", (Texture)texture);
        Set("targetBytes", new byte[pixels.Length * 4]); Set("targetColors", new Color32[pixels.Length]);
        Set("sourceColors", pixels);
        backing.SendCustomEvent("RequestBytes");
        deadline = EditorApplication.timeSinceStartup + 30;
    }

    static void Poll()
    {
        try
        {
            Require(EditorApplication.timeSinceStartup < deadline, "Readback timeout");
            if (!Get<bool>("completed")) return;
            Require(Get<bool>("bytesValid") && Get<bool>("colorsValid"), "TryGetData failure");
            byte[] actual = Get<byte[]>("targetBytes");
            Color32[] colors = Get<Color32[]>("targetColors");
            Color32[] expected = Get<Color32[]>("sourceColors");
            Require(colors.SequenceEqual(expected), "Color32 readback mismatch");
            for (int i = 0; i < colors.Length; i++)
                Require(actual[i * 4] == colors[i].r && actual[i * 4 + 1] == colors[i].g &&
                    actual[i * 4 + 2] == colors[i].b && actual[i * 4 + 3] == colors[i].a, "Byte channel ordering");
            var payload = new byte[ByteCounts[readbackCase]];
            Set("sourceBytes", actual); Set("targetBytes", payload);
            Set("sourceOffset", 56); Set("targetOffset", 0); Set("count", payload.Length);
            Call("BulkBytes");
            Require(payload.SequenceEqual(actual.Skip(56).Take(payload.Length)), "Readback payload offset/padding");
            lines.Add("PASS Udon LoadRawTextureData + byte[] GPU readback: payload=" + payload.Length + "; textureBytes=" + actual.Length + "; header=56; exact channels and payload");
            Object.Destroy(texture);
            readbackCase++;
            if (readbackCase == ByteCounts.Length) Finish(null);
            else BeginReadback();
        }
        catch (Exception error) { Finish(error); }
    }

    static void Finish(Exception error)
    {
        EditorApplication.update -= Poll;
        string directory = Environment.GetEnvironmentVariable("TSMP_FEASIBILITY_RESULTS");
        File.WriteAllLines(Path.Combine(directory, "bulk-times.csv"), timings);
        File.WriteAllText(Path.Combine(directory, "bulk-result.txt"), (error == null ? "PASS\n" : "FAIL\n") + string.Join("\n", lines) + "\n" + error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
#endif
