#if !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public sealed class SnapshotFormatProbe : MonoBehaviour
{
    readonly List<string> rows = new List<string>();
    Color[] reference;

#if UNITY_EDITOR
    public static void Gamma() { Launch(ColorSpace.Gamma); }
    public static void Linear() { Launch(ColorSpace.Linear); }
    static void Launch(ColorSpace space)
    {
        PlayerSettings.colorSpace = space;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Snapshot format probe").AddComponent<SnapshotFormatProbe>();
        new GameObject("Camera").AddComponent<Camera>().cullingMask = 0;
        EditorApplication.EnterPlaymode();
    }
#endif

    IEnumerator Start()
    {
        rows.Add("color_space,source_format,source_linear,target_format,target_readwrite,different_channels,max_abs_error");
        var work = CheckFormats();
        while (true)
        {
            bool next;
            try { next = work.MoveNext(); }
            catch (Exception error) { Finish(error); yield break; }
            if (!next) break;
            yield return work.Current;
        }
        Finish(null);
    }

    IEnumerator CheckFormats()
    {
        foreach (TextureFormat sourceFormat in new[] { TextureFormat.RGBA32, TextureFormat.RGBAHalf, TextureFormat.RGBAFloat })
        foreach (bool linear in new[] { false, true })
        {
            var texture = new Texture2D(256, 4, sourceFormat, false, linear) { filterMode = FilterMode.Point };
            var values = Enumerable.Range(0, 1024).Select(i =>
                sourceFormat == TextureFormat.RGBA32
                    ? new Color((i % 256) / 255f, ((i * 73) % 256) / 255f, ((i * 37) % 256) / 255f, 1)
                    : new Color(i * 0.001337f - 0.125f, i * 0.0004637f, 0.998765f - i * 0.000123f, 1)).ToArray();
            texture.SetPixels(values); texture.Apply(false, false);
            reference = null;
            foreach (RenderTextureFormat targetFormat in new[] { RenderTextureFormat.ARGBFloat, RenderTextureFormat.ARGBHalf, RenderTextureFormat.ARGB32 })
            foreach (RenderTextureReadWrite mode in new[] { RenderTextureReadWrite.Linear, RenderTextureReadWrite.sRGB })
            {
                if (mode == RenderTextureReadWrite.sRGB && targetFormat != RenderTextureFormat.ARGB32) continue;
                var target = new RenderTexture(256, 4, 0, targetFormat, mode) { filterMode = FilterMode.Point };
                if (!target.Create()) throw new InvalidOperationException("Cannot allocate " + targetFormat);
                var previous = RenderTexture.active;
                Graphics.Blit(texture, target);
                RenderTexture.active = previous;
                var sampled = new RenderTexture(256, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                sampled.Create();
                Graphics.Blit(target, sampled);
                RenderTexture.active = previous;
                var request = AsyncGPUReadback.Request(sampled, 0);
                while (!request.done) yield return null;
                if (request.hasError) throw new InvalidOperationException("GPU readback error");
                Color[] actual = request.GetData<Color>().ToArray();
                if (reference == null) reference = actual;
                double max = 0;
                int differences = 0;
                for (int i = 0; i < actual.Length; i++)
                for (int channel = 0; channel < 4; channel++)
                {
                    double difference = Math.Abs(actual[i][channel] - reference[i][channel]);
                    max = Math.Max(max, difference);
                    if (difference > 0.0000001) differences++;
                }
                rows.Add(string.Join(",", QualitySettings.activeColorSpace, sourceFormat, linear, targetFormat, mode,
                    differences, max.ToString("G9", CultureInfo.InvariantCulture)));
                target.Release(); Destroy(target);
                sampled.Release(); Destroy(sampled);
            }
            Destroy(texture);
        }
    }

    void Finish(Exception error)
    {
        string directory = Environment.GetEnvironmentVariable("TSMP_FEASIBILITY_RESULTS");
        File.WriteAllLines(Path.Combine(directory, "snapshot-formats.csv"), rows);
        File.WriteAllText(Path.Combine(directory, "snapshot-result.txt"), (error == null ? "PASS" : "FAIL") +
            "\nUnity=" + Application.unityVersion + "; GPU=" + SystemInfo.graphicsDeviceName + "; API=" + SystemInfo.graphicsDeviceType +
            "; ColorSpace=" + QualitySettings.activeColorSpace + "\nCompared sampled pixels against ARGBFloat-linear baseline; not a codec fidelity or performance benchmark.\n" + error);
#if UNITY_EDITOR
        EditorApplication.Exit(error == null ? 0 : 1);
#else
        Application.Quit(error == null ? 0 : 1);
#endif
    }
}
#endif
