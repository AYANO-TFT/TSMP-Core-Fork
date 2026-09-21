using UnityEngine;

namespace K13A.TSMP
{
    public static class GpuLuma4Writer
    {
        public static bool TryWrite(RenderTexture output, Material material, int blockSize, int payloadStartRow,
            byte[] header, byte[] payload, int payloadCount, float background,
            ref Texture2D upload, ref byte[] uploadBytes, ref RenderTexture symbols)
        {
            if (output == null || material == null || blockSize <= 0 || header == null || header.Length != FrameHeader.Size
                || payloadCount < 0 || (payloadCount > 0 && (payload == null || payloadCount > payload.Length)))
                return false;
            int widthBlocks = output.width / blockSize;
            int heightBlocks = output.height / blockSize;
            if (output.width % blockSize != 0 || output.height % blockSize != 0 || widthBlocks < 38
                || payloadStartRow < Luma4Raster.PayloadStartRow || heightBlocks <= payloadStartRow
                || payloadCount > widthBlocks * (heightBlocks - payloadStartRow - 1) / 2)
                return false;
#if !COMPILER_UDONSHARP
            if (!material.shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32))
                return false;
            RenderTexture previous = RenderTexture.active;
#endif
            int byteCount = FrameHeader.Size + payloadCount;
            int uploadHeight = Mathf.Max(1, (byteCount + 1023) / 1024);
            if (upload == null || upload.height < uploadHeight)
            {
                ReleaseTexture(upload);
                upload = new Texture2D(256, uploadHeight, TextureFormat.RGBA32, false, true);
                upload.name = "TSMP Luma4 Byte Upload";
                upload.filterMode = FilterMode.Point;
                upload.wrapMode = TextureWrapMode.Clamp;
                uploadBytes = new byte[1024 * uploadHeight];
            }
            if (symbols == null || symbols.width != widthBlocks || symbols.height != heightBlocks)
            {
                DecoderSnapshotRuntime.Release(symbols);
                symbols = new RenderTexture(widthBlocks, heightBlocks, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                symbols.name = "TSMP Luma4 Symbols";
                symbols.filterMode = FilterMode.Point;
                symbols.wrapMode = TextureWrapMode.Clamp;
                symbols.useMipMap = false;
                symbols.autoGenerateMips = false;
            }
            if (!symbols.IsCreated() && !symbols.Create())
                return false;
            System.Array.Copy(header, 0, uploadBytes, 0, FrameHeader.Size);
            if (payloadCount > 0)
                System.Array.Copy(payload, 0, uploadBytes, FrameHeader.Size, payloadCount);
            upload.LoadRawTextureData(uploadBytes);
            upload.Apply(false, false);
            material.SetInt("_SourceBlockWidth", widthBlocks);
            material.SetInt("_SourceBlockHeight", heightBlocks);
            material.SetInt("_PayloadStartRow", payloadStartRow);
            material.SetInt("_PayloadBytes", payloadCount);
            material.SetFloat("_Background", background);
            material.SetFloat("_BlockSize", blockSize);
            material.SetFloat("_OutputWidth", output.width);
            material.SetFloat("_OutputHeight", output.height);
            GraphicsBridge.Blit(upload, symbols, material, 0);
            GraphicsBridge.Blit(symbols, output, material, 1);
#if !COMPILER_UDONSHARP
            RenderTexture.active = previous;
#endif
            return true;
        }

        public static void ReleaseTexture(Texture2D texture)
        {
            if (texture == null)
                return;
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
                Object.DestroyImmediate(texture);
            else
                Object.Destroy(texture);
#else
            Object.Destroy(texture);
#endif
        }
    }
}
