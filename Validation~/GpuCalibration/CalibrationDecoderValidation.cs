using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
#endif

public sealed class CalibrationDecoderValidation : MonoBehaviour
{
    public TSMPCodec[] prefabs;
    int frames;

#if UNITY_EDITOR
    static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var runner = new GameObject("Calibration decoder validation").AddComponent<CalibrationDecoderValidation>();
        runner.prefabs = new[] { "Luma4", "RGB16", "RGB20", "Color256" }.Select(name =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec." +
                name.ToLowerInvariant() + "/Runtime/Codec_" + name + ".prefab").GetComponent<TSMPCodec>()).ToArray();
        new GameObject("Camera").AddComponent<Camera>().cullingMask = 0;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/GpuCalibration/Decoder.unity");
    }

    public static void Play()
    {
        CreateScene();
        EditorApplication.EnterPlaymode();
    }

    public static void Build()
    {
        CreateScene();
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        string path = Environment.GetEnvironmentVariable("TSMP_VALIDATION_BUILD");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/GpuCalibration/Decoder.unity" },
            locationPathName = path,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development
        });
        File.WriteAllText(Path.ChangeExtension(path, ".build-report.txt"),
            "Result=" + report.summary.result + "\nErrors=" + report.summary.totalErrors +
            "\nWarnings=" + report.summary.totalWarnings + "\nBackend=Mono\nStripping=Disabled");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
#endif

    IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;
        var work = Validate();
        while (true)
        {
            bool next;
            try { next = work.MoveNext(); }
            catch (Exception error) { Finish("FAIL\n" + error, 1); yield break; }
            if (!next) break;
            yield return work.Current;
        }
        Finish("PASS\nHeader CRC and raw payload matched through TSMPDecoder: " + frames +
            " frames; LUT policy checked; Unity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName, 0);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    IEnumerator Validate()
    {
        Check(SystemInfo.supportsAsyncGPUReadback, "Async GPU readback unsupported");
        foreach (var prefab in prefabs)
        {
            var codec = Instantiate(prefab.gameObject).GetComponent<TSMPCodec>();
            var luma = codec.codecId == 0 ? codec : Instantiate(prefabs[0].gameObject).GetComponent<TSMPCodec>();
            var owner = new GameObject("Decoder");
            var decoder = owner.AddComponent<TSMPDecoder>();
            decoder.applyEveryFrame = false;
            decoder.skipDuplicateFrames = false;
            decoder.decodeSafetyMode = 2;
            decoder.debugLog = false;
            decoder.sampleSize = 4;
            decoder.payloadByteTexture = new RenderTexture(1024, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            decoder.payloadByteTexture.Create();
            decoder.codecHandlers = codec == luma ? new[] { codec } : new[] { luma, codec };
            bool disableLut = Environment.GetEnvironmentVariable("TSMP_GPU_DISABLE_LUT") == "1";
            if (disableLut)
                foreach (var handler in decoder.codecHandlers) handler.calibrationMaterial = null;
            var source = new Texture2D(1280, 720, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            decoder.sourceTexture = source;
            yield return null;
            for (int variant = 0; variant < codec.DecodeMaterialCount; variant++)
            {
                if (codec is TSMPCodecRGB16 rgb)
                {
                    rgb.rBits = variant < 2 ? 4 : 5;
                    rgb.gBits = variant < 2 ? 4 : 6;
                    rgb.bBits = 4;
                    rgb.localRefine = variant % 2 == 1;
                }
                if (codec is TSMPCodecColor256 color)
                {
                    color.robustChannelDecode = variant > 0;
                    color.localRefine = variant == 2;
                }
                foreach (int sample in new[] { 4, 1, 4 })
                {
                    byte[] bytes = Enumerable.Range(0, 1027).Select(i => (byte)(i * 73 + frames + 19)).ToArray();
                    var header = FrameHeader.CreateDefault();
                    header.ActiveWidthBlocks = 160;
                    header.ActiveHeightBlocks = 90;
                    header.BlockSize = 8;
                    header.CodecId = codec.codecId;
                    header.SymbolMode = (byte)codec.SymbolMode;
                    header.PayloadSize = (ushort)bytes.Length;
                    header.FrameIndex = (uint)++frames;
                    header.DecodeSampleSize = (byte)sample;
                    byte[] options = codec.GetCodecOptionBytes() ?? new byte[0];
                    header.CodecOptionLength = (byte)options.Length;
                    if (options.Length > 0) header.CodecOption0 = options[0];
                    if (options.Length > 1) header.CodecOption1 = options[1];
                    if (options.Length > 2) header.CodecOption2 = options[2];
                    if (options.Length > 3) header.CodecOption3 = options[3];
                    if (options.Length > 4) header.CodecOption4 = options[4];
                    var headerBytes = new byte[FrameHeader.Size];
                    header.WriteTo(headerBytes, 0);
                    Check(codec.TryWriteFrame(source, 8, headerBytes, bytes, out string error), error);
                    decoder.sampleSize = sample;
                    Debug.Log("BEGIN Decoder frame=" + frames + " time=" + Time.realtimeSinceStartup + " sample=" + sample);
                    decoder.DecodeNow();
                    float deadline = Time.realtimeSinceStartup + 30f;
                    while (decoder.readbackInFlight && Time.realtimeSinceStartup < deadline) yield return null;
                    Check(!decoder.readbackInFlight, "Readback timeout at frame " + frames);
                    Check(decoder.lastHeaderValid && decoder.lastFrameValid, codec.displayName + ": " + decoder.lastError);
                    Check(decoder.lastFrameIndex == header.FrameIndex, "Stale header: expected=" + header.FrameIndex + " actual=" + decoder.lastFrameIndex);
                    var actual = (byte[])typeof(TSMPDecoder).GetField("_payloadBytes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(decoder);
                    Check(bytes.SequenceEqual(actual), "Payload mismatch " + codec.displayName);
                    bool useLut = codec is TSMPCodecColor256 ? variant == 2 : sample > 1 && (!(codec is TSMPCodecRGB16) || variant % 2 == 0);
                    useLut &= !disableLut;
                    Check(codec.selectedDecodeMaterial.IsKeywordEnabled("TSMP_CALIBRATION_LUT") == useLut, "Decoder LUT branch " + codec.displayName);
                    Debug.Log("PASS Decoder " + codec.displayName + " variant=" + variant + " sample=" + sample);
                }
            }
            RenderTexture.active = null;
            decoder.payloadByteTexture.Release();
            Destroy(decoder.payloadByteTexture);
            Destroy(owner);
            Destroy(source);
            Destroy(codec.gameObject);
            if (luma != codec) Destroy(luma.gameObject);
            yield return null;
        }
    }

    static void Finish(string result, int code)
    {
        Debug.Log(result);
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_GPU_RESULTS"), result);
#if UNITY_EDITOR
        EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
