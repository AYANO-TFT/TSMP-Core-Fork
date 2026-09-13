using System;
using K13A.TSMP;
using UnityEngine;
using Object = UnityEngine.Object;

public static class BlockExpansionCases
{
    public static void Run(Material material)
    {
        if (material == null || !material.shader.isSupported) throw new InvalidOperationException("Block expansion shader unavailable");
        int[,] sizes = { { 640, 360, 8 }, { 641, 361, 8 }, { 1920, 1080, 16 }, { 1920, 1088, 16 } };
        for (int i = 0; i < sizes.GetLength(0); i++)
        foreach (var colorSpace in new[] { RenderTextureReadWrite.Linear, RenderTextureReadWrite.sRGB })
            CheckFrame(material, sizes[i, 0], sizes[i, 1], sizes[i, 2], colorSpace);
    }

    private static void CheckFrame(Material material, int width, int height, int blockSize, RenderTextureReadWrite colorSpace)
    {
        int bw = width / blockSize;
        int bh = height / blockSize;
        var header = FrameHeader.CreateDefault();
        header.BlockSize = (ushort)blockSize;
        header.ActiveWidthBlocks = (ushort)bw;
        header.ActiveHeightBlocks = (ushort)bh;
        var payload = new byte[Luma4Raster.GetPayloadCapacityBytes(width, height, blockSize)];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(i * 37 + 13);
        header.PayloadSize = (ushort)payload.Length;
        var headerBytes = new byte[FrameHeader.Size];
        header.WriteTo(headerBytes, 0);
        var colors = SymbolCodec.CreateLuma4Colors();
        var symbols = new Color32[bw * bh];
        var pixels = new Color32[width * height];
        EncoderUdonTextureRuntime.ClearPixelBuffer(symbols);
        EncoderUdonTextureRuntime.ClearPixelBuffer(pixels);
        Luma4FrameTextureWriter.WriteBaseRegions(symbols, width, height, blockSize, bw, bh, true, headerBytes, colors);
        Luma4FrameTextureWriter.WritePayload(symbols, width, height, blockSize, bw, bh, true, Luma4Raster.PayloadStartRow, payload, payload.Length, colors);
        Luma4FrameTextureWriter.WriteBaseRegions(pixels, width, height, blockSize, bw, bh, false, headerBytes, colors);
        Luma4FrameTextureWriter.WritePayload(pixels, width, height, blockSize, bw, bh, false, Luma4Raster.PayloadStartRow, payload, payload.Length, colors);
        var blocks = Luma4Raster.CreateTexture(bw, bh);
        var full = Luma4Raster.CreateTexture(width, height);
        var expanded = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, colorSpace);
        var reference = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, colorSpace);
        Texture2D actual = null;
        Texture2D expected = null;
        try
        {
            blocks.SetPixels32(symbols);
            blocks.Apply();
            full.SetPixels32(pixels);
            full.Apply();
            expanded.Create();
            reference.Create();
            Graphics.Blit(Texture2D.whiteTexture, expanded);
            EncoderUdonTextureRuntime.BlitEncodedTexture(blocks, expanded, true, material, bw, bh, blockSize);
            Graphics.Blit(full, reference);
            actual = Read(expanded);
            expected = Read(reference);
            Color32[] a = actual.GetPixels32();
            Color32[] b = expected.GetPixels32();
            for (int i = 0; i < a.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) > 1 || Mathf.Abs(a[i].g - b[i].g) > 1 || Mathf.Abs(a[i].b - b[i].b) > 1 || a[i].a != b[i].a)
                    throw new InvalidOperationException("Expansion pixel mismatch " + width + "x" + height + "/" + blockSize + " " + colorSpace + " x=" + i % width + " y=" + i / width);
            if (!Luma4RasterReader.TryReadFrame(actual, blockSize, FrameHeader.Size, payload.Length, out var decodedHeader, out var decoded, out string error))
                throw new InvalidOperationException("Expanded frame decode failed: " + error);
            if (decodedHeader.PayloadSize != payload.Length || decoded.Length != payload.Length)
                throw new InvalidOperationException("Expanded payload length mismatch");
            for (int i = 0; i < payload.Length; i++)
                if (decoded[i] != payload[i]) throw new InvalidOperationException("Expanded payload mismatch at " + i);
            Debug.Log("[BlockExpansion] PASS " + width + "x" + height + "/" + blockSize + " " + colorSpace + ": every pixel, header CRC and " + payload.Length + " payload bytes");
        }
        finally
        {
            expanded.Release();
            reference.Release();
            Object.DestroyImmediate(expanded);
            Object.DestroyImmediate(reference);
            Object.DestroyImmediate(blocks);
            Object.DestroyImmediate(full);
            Object.DestroyImmediate(actual);
            Object.DestroyImmediate(expected);
        }
    }

    private static Texture2D Read(RenderTexture source)
    {
        var old = RenderTexture.active;
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
        try
        {
            RenderTexture.active = source;
            texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            texture.Apply();
            return texture;
        }
        finally { RenderTexture.active = old; }
    }
}
