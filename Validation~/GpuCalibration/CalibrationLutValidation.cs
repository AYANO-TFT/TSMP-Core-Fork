#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using K13A.TSMP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public sealed class CalibrationLutValidation : MonoBehaviour
{
    const string Folder = "Assets/GpuCalibration/Generated";
    const int Repeats = 32;
    const string Keyword = "TSMP_CALIBRATION_LUT";
    static readonly string[] Codecs = { "Luma4", "RGB16", "RGB16", "RGB16", "RGB16", "RGB20", "Color256" };
    static readonly string[] Shaders = { "TSMPDecodeLuma4Bytes", "TSMPDecodeRgb16Bytes",
        "TSMPDecodeRgb16RefineBytes", "TSMPDecodeRgb16VariableBytes", "TSMPDecodeRgb16VariableRefineBytes",
        "TSMPDecodeRgb20Bytes", "TSMPDecodeColor256RobustRefineBytes" };
    readonly List<string> results = new List<string>();
    static string Output => Environment.GetEnvironmentVariable("TSMP_GPU_RESULTS");

    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        for (int i = 0; i < Shaders.Length; i++)
        {
            string package = "Packages/com.kibalab.tsmp.codec." + Codecs[i].ToLowerInvariant();
            string root = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(package).resolvedPath;
            string original = File.ReadAllText(Path.GetFullPath(root + "/../../Validation~/Gpu/LutBaselines/" + Shaders[i] + ".shader.txt"));
            original = Regex.Replace(original, "Shader \"[^\"]+\"", "Shader \"Hidden/CalibrationBaseline/" + i + "\"");
            File.WriteAllText(Folder + "/Baseline" + i + ".shader", original);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool("TSMP.CalibrationLutValidation", true);
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
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("TSMP.CalibrationLutValidation", false))
            return;
        SessionState.SetBool("TSMP.CalibrationLutValidation", false);
        new GameObject("Calibration LUT Validation").AddComponent<CalibrationLutValidation>();
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, true);
        Profiler.enabled = true;
        new GameObject("GPU Frame Pump").AddComponent<Camera>().cullingMask = 0;
        results.Add("Unity=" + Application.unityVersion + ", GPU=" + SystemInfo.graphicsDeviceName + ", API=" + SystemInfo.graphicsDeviceType);
        var stack = new Stack<IEnumerator>();
        stack.Push(Validate());
        while (stack.Count > 0)
        {
            IEnumerator work = stack.Peek();
            bool next;
            try { next = work.MoveNext(); }
            catch (Exception error) { results.Add("FAIL " + error); Finish(1); yield break; }
            if (!next) { stack.Pop(); continue; }
            if (work.Current is IEnumerator nested) { stack.Push(nested); continue; }
            yield return work.Current;
        }
        Finish(0);
    }

    void Finish(int code)
    {
        results.Insert(0, code == 0 ? "PASS" : "FAIL");
        Directory.CreateDirectory(Path.GetDirectoryName(Output));
        File.WriteAllLines(Output, results);
        Debug.Log(string.Join("\n", results));
        Profiler.enabled = false;
        EditorApplication.Exit(code);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static RenderTexture Cache(TSMPCodec codec)
    {
        return (RenderTexture)typeof(TSMPCodec).GetField("_calibrationLut", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(codec);
    }

    IEnumerator Validate()
    {
        for (int id = 0; id < Shaders.Length; id++)
        {
            string package = "Packages/com.kibalab.tsmp.codec." + Codecs[id].ToLowerInvariant();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(package + "/Runtime/Codec_" + Codecs[id] + ".prefab");
            var instance = Object.Instantiate(prefab);
            var codec = instance.GetComponent<TSMPCodec>();
            if (codec is TSMPCodecRGB16 rgb)
            {
                rgb.rBits = id <= 2 ? 4 : 5;
                rgb.gBits = id <= 2 ? 4 : 6;
                rgb.bBits = 4;
                rgb.localRefine = id == 2 || id == 4;
            }
            if (codec is TSMPCodecColor256 color)
            {
                color.robustChannelDecode = true;
                color.localRefine = true;
            }
            var source = new Texture2D(1280, 720, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point };
            byte[] payload = Enumerable.Range(0, 4096).Select(i => (byte)(i * 73 + 19)).ToArray();
            Check(codec.TryWriteFrame(source, 8, new byte[56], payload, out string error), error);
            var noisy = Noise(source, 311);
            var flat = new Texture2D(1280, 720, TextureFormat.RGBAFloat, false, true);
            flat.SetPixels(Enumerable.Repeat(new Color(.35f, .35f, .35f, 1), 1280 * 720).ToArray());
            flat.Apply();
            var baseline = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Folder + "/Baseline" + id + ".shader"));
            var candidate = new Material(AssetDatabase.LoadAssetAtPath<Shader>(package + "/Runtime/Shaders/" + Shaders[id] + ".shader"));
            if (codec is TSMPCodecRGB16 refinedRgb)
            {
                if (id == 2) refinedRgb.refine444ByteDecodeMaterial = candidate;
                if (id == 4) refinedRgb.variableRefineByteDecodeMaterial = candidate;
            }
            if (codec is TSMPCodecColor256 refine) refine.robustRefineByteDecodeMaterial = candidate;
            codec.calibrationMaterial = new Material(codec.calibrationMaterial);
            var prepare = codec.calibrationMaterial;
            var output = Target(4096, RenderTextureFormat.ARGB32);
            int start = codec.GetPayloadStartRow(1280, 8) * 160;
            int cases = 0;
            RenderTexture previousCache = null;
            foreach (int sample in new[] { 1, 0, 2, 3, 4, 8 })
            foreach (var input in new[] { source, noisy, flat })
            foreach (int flip in new[] { 1, 0 })
            {
                foreach (var material in new[] { baseline, candidate })
                {
                    Setup(material, input, 4096, start, sample);
                    material.SetFloat("_FlipY", flip);
                    if (id <= 2) { material.SetFloat("_RBits", 4); material.SetFloat("_GBits", 4); }
                }
                candidate.DisableKeyword(Keyword);
                Graphics.Blit(input, output, baseline);
                byte[] expected = Read(output);
                Graphics.Blit(input, output, candidate);
                Check(expected.SequenceEqual(Read(output)), "Original branch changed: " + Shaders[id]);
                codec.PrepareDecode(input, candidate);
                bool enabled = (sample != 1 && id != 2 && id != 4) || id == 6;
                Check(candidate.IsKeywordEnabled(Keyword) == enabled, "Wrong adaptive branch: " + Shaders[id]);
                if (enabled)
                {
                    RenderTexture cache = Cache(codec);
                    Check(cache != null && cache.IsCreated() && cache.format == RenderTextureFormat.ARGBFloat, "Missing Float32 cache");
                    Check(cache.height == 1 && cache.width == (id == 0 ? 16 : id <= 2 ? 48 : id <= 4 ? 112 : id == 5 ? 320 : 276), "Incorrect cache dimensions");
                    if (previousCache != null) Check(cache == previousCache, "Cache allocated every pass");
                    previousCache = cache;
                }
                Graphics.Blit(input, output, candidate);
                byte[] actual = Read(output);
                Check(expected.SequenceEqual(actual), "LUT bytes differ: " + Shaders[id] + " sample=" + sample + " flip=" + flip);
                if (input == source && flip == 1) Check(payload.SequenceEqual(actual.Take(payload.Length)), "Exact round-trip failed");
                cases++;
            }

            foreach (int count in new[] { 0, 1, 2, 3, 4, 5, 7, 16, 31, 56, 257, 1027 })
            {
                foreach (var material in new[] { baseline, candidate }) Setup(material, source, count, start, 4);
                if (id <= 2) foreach (var material in new[] { baseline, candidate })
                { material.SetFloat("_RBits", 4); material.SetFloat("_GBits", 4); }
                Graphics.Blit(source, output, baseline);
                byte[] expected = Read(output);
                codec.PrepareDecode(source, candidate);
                Graphics.Blit(source, output, candidate);
                Check(expected.SequenceEqual(Read(output)), "Partial output differs: " + Shaders[id] + " count=" + count);
                if (count == 0) Check(!candidate.IsKeywordEnabled(Keyword), "Empty output must skip the prepass");
                cases++;
            }

            codec.calibrationMaterial = null;
            codec.PrepareDecode(source, candidate);
            Check(!candidate.IsKeywordEnabled(Keyword), "Missing prepass did not fall back");
            codec.calibrationMaterial = prepare;
            if (id == 3)
            {
                candidate.SetFloat("_RBits", 8);
                candidate.SetFloat("_GBits", 8);
                codec.PrepareDecode(source, candidate);
                Check(Cache(codec) == previousCache && Cache(codec).width == 528, "Variable-bit cache resize failed");
            }

            results.Add("PASS " + Shaders[id] + ": " + cases + " original/LUT GPU comparisons, branch selection, reuse, fallback and dimensions");
            Debug.Log(results.Last());
            foreach (int block in new[] { 1, 2, 4, 8, 16 })
            foreach (int requested in new[] { 0, 1, 2, 3, 8, 16 })
            {
                candidate.SetFloat("_BlockSize", block);
                candidate.SetFloat("_SampleSize", requested);
                int samples = Mathf.Clamp(requested == 0 ? (block >= 8 ? 4 : 3) : requested, 1, Mathf.Min(block, 8));
                codec.PrepareDecode(source, candidate);
                bool enabled = (samples > 1 && id != 2 && id != 4) || id == 6;
                Check(candidate.IsKeywordEnabled(Keyword) == enabled, "Effective sample selection");
            }
            if (Environment.GetEnvironmentVariable("TSMP_GPU_SKIP_TIMING") != "1")
            foreach (int sample in new[] { 1, 2, 3, 4 })
            foreach (int count in new[] { 4, 56, 1024 })
            {
                foreach (var material in new[] { baseline, candidate })
                {
                    Setup(material, source, count, start, sample);
                    if (id <= 2) { material.SetFloat("_RBits", 4); material.SetFloat("_GBits", 4); }
                }
                codec.PrepareDecode(source, candidate);
                var sa = CustomSampler.Create("Calibration.A." + id + "." + sample + "." + count, true);
                var sb = CustomSampler.Create("Calibration.B." + id + "." + sample + "." + count, true);
                var a = new CommandBuffer();
                var b = new CommandBuffer();
                a.BeginSample(sa);
                b.BeginSample(sb);
                for (int n = 0; n < Repeats; n++)
                {
                    a.Blit(source, output, baseline);
                    if (candidate.IsKeywordEnabled(Keyword)) b.Blit(source, Cache(codec), prepare);
                    b.Blit(source, output, candidate);
                }
                a.EndSample(sa);
                b.EndSample(sb);
                yield return Measure(results, Shaders[id] + ",sample=" + sample + ",bytes=" + count, a, b, Repeats, output, sa, sb);
                a.Dispose();
                b.Dispose();
            }

            codec.enabled = false;
            yield return null;
            Check(Cache(codec) == null && !candidate.IsKeywordEnabled(Keyword), "Disable did not release cache/binding");
            codec.enabled = true;
            candidate.SetFloat("_BlockSize", 8);
            candidate.SetFloat("_SampleSize", 4);
            codec.PrepareDecode(source, candidate);
            Check(candidate.IsKeywordEnabled(Keyword) == (id != 2 && id != 4), "Re-enable branch selection");
            if (id != 2 && id != 4) Check(Cache(codec) != null, "Re-enable did not restore cache");
            if (id == 0)
            {
                var other = Object.Instantiate(prefab).GetComponent<TSMPCodec>();
                other.PrepareDecode(noisy, candidate);
                Check(Cache(other) != Cache(codec), "Codec instances share a mutable LUT");
                codec.enabled = false;
                Check(candidate.IsKeywordEnabled(Keyword) && candidate.GetTexture("_CalibrationLut") == Cache(other),
                    "Disabling one codec cleared another codec's binding");
                other.enabled = false;
                Check(!candidate.IsKeywordEnabled(Keyword), "Last owner did not clear the shared material");
                Object.Destroy(other.gameObject);
                codec.enabled = true;
                codec.PrepareDecode(source, candidate);
                var alternate = new Material(candidate);
                codec.PrepareDecode(source, alternate);
                Check(!candidate.IsKeywordEnabled(Keyword), "Material switch left an old LUT enabled");
                codec.PrepareDecode(source, candidate);
                Check(!alternate.IsKeywordEnabled(Keyword), "Returning to a material left an old LUT enabled");
                Object.Destroy(alternate);
            }
            Object.Destroy(instance);
            yield return null;
            Check(!candidate.IsKeywordEnabled(Keyword), "Destroy left cached material enabled");
            RenderTexture.active = null;
            output.Release();
            foreach (Object item in new Object[] { source, noisy, flat, baseline, candidate, prepare, output }) Object.Destroy(item);
        }
    }

    static RenderTexture Target(int width, RenderTextureFormat format)
    {
        var texture = new RenderTexture(width, 1, 0, format, RenderTextureReadWrite.Linear)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        return texture;
    }

    static void Setup(Material material, Texture source, int count, int start, int sample, int width = 4096)
    {
        material.SetTexture("_MainTex", source);
        material.SetFloat("_BlockSize", 8);
        material.SetFloat("_SampleSize", sample);
        material.SetFloat("_StartBlock", start);
        material.SetFloat("_ByteCount", count);
        material.SetFloat("_ActiveWidthBlocks", 160);
        material.SetFloat("_SourceWidth", 1280);
        material.SetFloat("_SourceHeight", 720);
        material.SetFloat("_OutputWidth", width);
        material.SetFloat("_OutputHeight", 1);
        material.SetFloat("_FlipY", 1);
        material.SetFloat("_Rgb16CalibrationStartBlock", 800);
        material.SetFloat("_Rgb20CalibrationStartBlock", 800);
        material.SetFloat("_ColorCalibrationStartBlock", 800);
        material.SetFloat("_RBits", 5);
        material.SetFloat("_GBits", 6);
        material.SetFloat("_BBits", 4);
        material.SetFloat("_RefineRadius", 2);
    }

    static byte[] Read(RenderTexture target)
    {
        var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
        request.WaitForCompletion();
        Check(!request.hasError, "GPU readback failed");
        return request.GetData<byte>().ToArray();
    }

    static Texture2D Noise(Texture2D source, int seed)
    {
        Color[] pixels = source.GetPixels();
        var random = new System.Random(seed);
        for (int i = 0; i < pixels.Length; i++)
        {
            Color c = pixels[i];
            c.r = Mathf.Clamp01(c.r * 0.9431f + 0.0247f + (float)(random.NextDouble() - .5) * .008f);
            c.g = Mathf.Clamp01(c.g * 0.9613f + 0.0173f + (float)(random.NextDouble() - .5) * .008f);
            c.b = Mathf.Clamp01(c.b * 0.9217f + 0.0311f + (float)(random.NextDouble() - .5) * .008f);
            pixels[i] = c;
        }
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true)
        {
            filterMode = FilterMode.Point
        };
        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    static IEnumerator Measure(List<string> results, string label, CommandBuffer a, CommandBuffer b,
        int repeats, RenderTexture output, CustomSampler samplerA, CustomSampler samplerB)
    {
        var recorderA = samplerA.GetRecorder();
        var recorderB = samplerB.GetRecorder();
        recorderA.enabled = true;
        recorderB.enabled = true;
        var gpuA = new List<double>();
        var gpuB = new List<double>();
        var wallA = new List<double>();
        var wallB = new List<double>();
        for (int frame = 0; frame < 25; frame++)
        {
            double elapsedA;
            double elapsedB;
            if ((frame & 1) == 0)
            {
                elapsedA = Submit(a, output);
                elapsedB = Submit(b, output);
            }
            else
            {
                elapsedB = Submit(b, output);
                elapsedA = Submit(a, output);
            }
            yield return null;
            if (frame < 10) continue;
            wallA.Add(elapsedA / repeats);
            wallB.Add(elapsedB / repeats);
            if (recorderA.gpuSampleBlockCount == 1 && recorderB.gpuSampleBlockCount == 1 &&
                recorderA.gpuElapsedNanoseconds > 0 && recorderB.gpuElapsedNanoseconds > 0)
            {
                gpuA.Add(recorderA.gpuElapsedNanoseconds / 1000.0 / repeats);
                gpuB.Add(recorderB.gpuElapsedNanoseconds / 1000.0 / repeats);
            }
        }
        recorderA.enabled = false;
        recorderB.enabled = false;
        Check(gpuA.Count >= 10, "No valid GPU timing: " + label);
        gpuA.Sort();
        gpuB.Sort();
        wallA.Sort();
        wallB.Sort();
        string line = $"TIMING,{label},A_us={gpuA[gpuA.Count / 2]:F3},B_us={gpuB[gpuB.Count / 2]:F3}," +
            $"wall_A_us={wallA[wallA.Count / 2]:F3},wall_B_us={wallB[wallB.Count / 2]:F3},samples={gpuA.Count},batch={repeats}";
        results.Add(line);
        Debug.Log(line);
    }

    static double Submit(CommandBuffer commands, RenderTexture output)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Graphics.ExecuteCommandBuffer(commands);
        Read(output);
        watch.Stop();
        return watch.Elapsed.TotalMilliseconds * 1000.0;
    }
}
#endif
