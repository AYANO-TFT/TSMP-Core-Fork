using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace K13A.TSMP
{
    public static class DecoderSnapshotRuntime
    {
        public static RenderTexture Capture(Texture source, RenderTexture snapshot, out string error)
        {
            return CaptureTarget(source, snapshot, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear, out error);
        }

        public static RenderTexture CaptureMatchingFormat(Texture source, RenderTexture snapshot, int previousSourceFormat, out int sourceFormat, out string error)
        {
            RenderTextureReadWrite readWrite;
            RenderTextureFormat format = GetCaptureFormat(source, out readWrite);
            sourceFormat = (int)format * 4 + (int)readWrite;
            if (sourceFormat != previousSourceFormat)
            {
                Release(snapshot);
                snapshot = null;
            }
            return CaptureTarget(source, snapshot, format, readWrite, out error);
        }

        private static RenderTexture CaptureTarget(Texture source, RenderTexture snapshot, RenderTextureFormat format, RenderTextureReadWrite readWrite, out string error)
        {
            error = string.Empty;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                error = "Source texture is not available for a decode snapshot.";
                Release(snapshot);
                return null;
            }

#if !COMPILER_UDONSHARP
            if (!SystemInfo.SupportsRenderTextureFormat(format))
            {
                format = RenderTextureFormat.ARGBFloat;
                readWrite = RenderTextureReadWrite.Linear;
            }
            if (!SystemInfo.SupportsRenderTextureFormat(format))
            {
                error = "Float32 decode snapshots are not supported on this graphics device.";
                Release(snapshot);
                return null;
            }
#endif

            if (snapshot != null && snapshot.format != format)
            {
                Release(snapshot);
                snapshot = null;
            }
            if (snapshot == null)
            {
                snapshot = new RenderTexture(source.width, source.height, 0, format, readWrite);
                snapshot.name = "TSMP Decoder Source Snapshot";
                snapshot.useMipMap = false;
                snapshot.autoGenerateMips = false;
                snapshot.antiAliasing = 1;
            }
            else if (snapshot.width != source.width || snapshot.height != source.height)
            {
                snapshot.Release();
                snapshot.width = source.width;
                snapshot.height = source.height;
            }

            if (!snapshot.IsCreated() && !snapshot.Create())
            {
                error = "Failed to create decode snapshot. width=" + source.width + " height=" + source.height;
                Release(snapshot);
                return null;
            }

            snapshot.filterMode = source.filterMode;
            snapshot.wrapMode = source.wrapMode;
#if !COMPILER_UDONSHARP
            RenderTexture previousTarget = RenderTexture.active;
#endif
            GraphicsBridge.Blit(source, snapshot);
#if !COMPILER_UDONSHARP
            RenderTexture.active = previousTarget;
#endif
            return snapshot;
        }

        public static RenderTextureFormat GetCaptureFormat(Texture source, out RenderTextureReadWrite readWrite)
        {
            readWrite = RenderTextureReadWrite.Linear;
            if (source == null)
                return RenderTextureFormat.ARGBFloat;
#if COMPILER_UDONSHARP
            if (!source.GetType().Equals(typeof(RenderTexture)))
                return RenderTextureFormat.ARGBFloat;
            RenderTexture target = (RenderTexture)source;
            RenderTextureFormat targetFormat = target.format;
            if (targetFormat == RenderTextureFormat.ARGB32 || targetFormat == RenderTextureFormat.BGRA32)
            {
                if (target.sRGB)
                    readWrite = RenderTextureReadWrite.sRGB;
                return RenderTextureFormat.ARGB32;
            }
            if (targetFormat == RenderTextureFormat.ARGBHalf)
                return RenderTextureFormat.ARGBHalf;
#else
            GraphicsFormat format = source.graphicsFormat;
            if (format == GraphicsFormat.R8G8B8A8_SRGB || format == GraphicsFormat.B8G8R8A8_SRGB)
            {
                readWrite = RenderTextureReadWrite.sRGB;
                return RenderTextureFormat.ARGB32;
            }
            if (format == GraphicsFormat.R8G8B8A8_UNorm || format == GraphicsFormat.B8G8R8A8_UNorm)
                return RenderTextureFormat.ARGB32;
            if (format == GraphicsFormat.R16G16B16A16_SFloat)
                return RenderTextureFormat.ARGBHalf;
#endif
            return RenderTextureFormat.ARGBFloat;
        }

        public static void Release(RenderTexture snapshot)
        {
            if (snapshot == null)
                return;

            snapshot.Release();
#if UNITY_EDITOR && !COMPILER_UDONSHARP
            if (!Application.isPlaying)
                Object.DestroyImmediate(snapshot);
            else
                Object.Destroy(snapshot);
#else
            Object.Destroy(snapshot);
#endif
        }
    }
}
