#if UNITY_EDITOR && !COMPILER_UDONSHARP
using System;
using System.IO;
using System.Linq;
using K13A.TSMP;
using UnityEditor;
using UnityEngine;

public static class NativeRasterValidation
{
    public static void Run()
    {
        var root = new GameObject("Raster validation");
        var codec = root.AddComponent<TSMPCodecLuma4>();
        Color32[] first = null;
        Color32[] second = null;
        int cases = 0;
        foreach (int width in new[] { 640, 1280, 648, 640 })
        {
            var texture = Luma4Raster.CreateTexture(width, 360);
            var reference = Luma4Raster.CreateTexture(width, 360);
            foreach (int count in new[] { 1024, 0, 1, 3, 32, 5 })
            {
                byte[] payload = Enumerable.Range(0, count).Select(i => (byte)(i * 73)).ToArray();
                var header = FrameHeader.CreateDefault();
                header.BlockSize = 8;
                header.PayloadSize = (ushort)count;
                header.FrameIndex = (uint)++cases;
                byte[] bytes = new byte[FrameHeader.Size];
                header.WriteTo(bytes, 0);
                Color32[] previous = first;
                Require(codec.TryWriteFrameBuffered(texture, 8, bytes, payload, ref first, out string error), error);
                Require(codec.TryWriteFrame(reference, 8, bytes, payload, out error), error);
                Require(texture.GetPixels32().SequenceEqual(reference.GetPixels32()), "Cached raster differs from fresh raster");
                if (previous != null && previous.Length == width * 360)
                    Require(ReferenceEquals(previous, first), "Warm buffer reallocated");
                Require(codec.TryWriteFrameBuffered(reference, 8, bytes, payload, ref second, out error), error);
                Require(!ReferenceEquals(first, second), "Encoder-owned buffers aliased");
            }
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(reference);
        }
        UnityEngine.Object.DestroyImmediate(root);
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("TSMP_FEASIBILITY_RESULTS"), "raster-result.txt"),
            "PASS " + cases + " cached/fresh raster comparisons, empty and shrinking payloads, odd block width, resize, reuse and independent buffers");
        EditorApplication.Exit(0);
    }

    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
#endif
