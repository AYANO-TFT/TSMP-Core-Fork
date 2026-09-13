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
using UnityEngine.Rendering;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.Editor;
using Object = UnityEngine.Object;

public static class UdonCalibrationValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Keyword = "TSMP_CALIBRATION_LUT";

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
        SessionState.SetBool("TSMP.UdonCalibration", true);
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
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.UdonCalibration", false)) return;
        SessionState.SetBool("TSMP.UdonCalibration", false);
        EditorApplication.delayCall += Validate;
    }

    static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    static void Validate()
    {
        var results = new List<string> { "Unity=" + Application.unityVersion, "Full Udon client compile passed" };
        int exit = 0;
        try
        {
            foreach (string name in new[] { "Luma4", "RGB16", "RGB20", "Color256" })
                ValidateCodec(name, results);
            results.Insert(0, "PASS");
        }
        catch (Exception exception)
        {
            results.Add("FAIL " + exception);
            Debug.LogException(exception);
            exit = 1;
        }
        File.WriteAllLines(Environment.GetEnvironmentVariable("TSMP_GPU_RESULTS"), results);
        EditorApplication.Exit(exit);
    }

    static void ValidateCodec(string name, List<string> results)
    {
        string package = "Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant();
        var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(package + "/Runtime/Codec_" + name + ".prefab"));
        var codec = instance.GetComponent<TSMPCodec>();
        var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(package + "/Runtime/Scripts/TSMPCodec" + name + ".asset");
        asset.UpdateProgram();
        var program = asset.GetRealProgram();
        var vm = UdonEditorManager.Instance.ConstructUdonVM();
        vm.LoadProgram(program);
        var backing = new GameObject("Calibration VM").AddComponent<UdonBehaviour>();
        var source = new Texture2D(1280, 720, TextureFormat.RGBA32, false, true);
        var output = new RenderTexture(1024, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        output.Create();
        try
        {
            Check((bool)typeof(UdonBehaviour).GetMethod("ResolveUdonHeapReferences", Private)
                .Invoke(backing, new object[] { program.SymbolTable, program.Heap }), "Heap references");
            typeof(UdonBehaviour).GetField("_program", Private).SetValue(backing, program);
            typeof(UdonBehaviour).GetField("_udonVM", Private).SetValue(backing, vm);
            typeof(UdonBehaviour).GetField("_udonManager", Private).SetValue(backing, UdonManager.Instance);
            Set(program, "calibrationMaterial", codec.calibrationMaterial);
            for (int variant = 0; variant < codec.DecodeMaterialCount; variant++)
            {
                if (codec is TSMPCodecRGB16 rgb)
                {
                    rgb.rBits = variant < 2 ? 4 : 5;
                    rgb.gBits = variant < 2 ? 4 : 6;
                    rgb.bBits = 4;
                    rgb.localRefine = variant % 2 == 1;
                }
                byte[] payload = Enumerable.Range(0, 1027).Select(i => (byte)(i * 73 + 19)).ToArray();
                Check(codec.TryWriteFrame(source, 8, new byte[56], payload, out string error), error);
                var material = new Material(codec.GetDecodeMaterial(variant));
                if (codec is TSMPCodecColor256 color)
                    Set(program, "robustRefineByteDecodeMaterial", variant == 2 ? material : color.robustRefineByteDecodeMaterial);
                if (codec is TSMPCodecRGB16)
                {
                    if (variant == 1) Set(program, "refine444ByteDecodeMaterial", material);
                    if (variant == 3) Set(program, "variableRefineByteDecodeMaterial", material);
                }
                bool usesLut = name == "Color256" ? variant == 2 : name != "RGB16" || variant % 2 == 0;
                foreach (int sample in new[] { 4, 1, 4 })
                {
                    material.SetFloat("_BlockSize", 8);
                    material.SetFloat("_SampleSize", sample);
                    material.SetFloat("_StartBlock", codec.GetPayloadStartRow(1280, 8) * 160);
                    material.SetFloat("_ByteCount", payload.Length);
                    material.SetFloat("_ActiveWidthBlocks", 160);
                    material.SetFloat("_SourceWidth", 1280);
                    material.SetFloat("_SourceHeight", 720);
                    material.SetFloat("_OutputWidth", 1024);
                    material.SetFloat("_OutputHeight", 1);
                    material.SetFloat("_FlipY", 1);
                    material.SetFloat("_Rgb16CalibrationStartBlock", 800);
                    material.SetFloat("_Rgb20CalibrationStartBlock", 800);
                    material.SetFloat("_ColorCalibrationStartBlock", 800);
                    material.SetFloat("_RBits", variant < 2 ? 4 : 5);
                    material.SetFloat("_GBits", variant < 2 ? 4 : 6);
                    material.SetFloat("_BBits", 4);
                    material.SetFloat("_RefineRadius", 2);
                    material.DisableKeyword(Keyword);
                    Graphics.Blit(source, output, material);
                    var direct = Read(output);
                    Set<Texture>(program, "__0_source__param", source);
                    Set(program, "__0_material__param", material);
                    Call(program, vm, "__0_PrepareDecode");
                    Check(material.IsKeywordEnabled(Keyword) == (usesLut && (sample != 1 || name == "Color256")), "VM branch " + name);
                    Graphics.Blit(source, output, material);
                    Check(direct.SequenceEqual(Read(output)), "VM LUT parity " + name + " variant=" + variant);
                    var lut = (RenderTexture)program.Heap.GetHeapVariable(program.SymbolTable.GetAddressFromSymbol("_calibrationLut"));
                    Check(!usesLut || (lut != null && lut.IsCreated() && lut.format == RenderTextureFormat.ARGBFloat), "VM Float32 allocation");
                }
                RenderTexture.active = null;
                Call(program, vm, "_onDisable");
                Check(!material.IsKeywordEnabled(Keyword), "VM disable cleanup");
                Call(program, vm, "_onDestroy");
                Object.DestroyImmediate(material);
            }
            results.Add("PASS " + name + ": actual Udon VM PrepareDecode, Float32 allocation, prepass, shader byte parity, fallback and cleanup");
        }
        finally
        {
            RenderTexture.active = null;
            output.Release();
            foreach (Object item in new Object[] { source, output, instance, backing.gameObject }) Object.DestroyImmediate(item);
        }
    }

    static byte[] Read(RenderTexture texture)
    {
        var request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32);
        request.WaitForCompletion();
        Check(!request.hasError, "GPU readback");
        return request.GetData<byte>().ToArray();
    }

    static void Set<T>(IUdonProgram program, string name, T value)
    {
        program.Heap.SetHeapVariable(program.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
    }

    static void Call(IUdonProgram program, IUdonVM vm, string name)
    {
        vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol(name));
        Check(vm.Interpret() == 0, "Udon VM event " + name);
    }
}
#endif
