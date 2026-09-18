using UnityEngine;

namespace K13A.TSMP
{
    public static class DecoderSnapshotRuntime
    {
        public static RenderTexture Capture(Texture source, RenderTexture snapshot, out string error)
        {
            error = string.Empty;
            if (source == null || source.width <= 0 || source.height <= 0)
            {
                error = "Source texture is not available for a decode snapshot.";
                Release(snapshot);
                return null;
            }

#if !COMPILER_UDONSHARP
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            {
                error = "Float32 decode snapshots are not supported on this graphics device.";
                Release(snapshot);
                return null;
            }
#endif

            if (snapshot == null)
            {
                snapshot = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
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

            if (snapshot.format != RenderTextureFormat.ARGBFloat || (!snapshot.IsCreated() && !snapshot.Create()))
            {
                error = "Failed to create Float32 decode snapshot. width=" + source.width + " height=" + source.height;
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
