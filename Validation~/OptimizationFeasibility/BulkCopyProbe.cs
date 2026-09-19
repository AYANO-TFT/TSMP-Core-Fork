using System;
using K13A.TSMP;
using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.SDK3.Rendering;
#endif

public sealed class BulkCopyProbe : TSMPBehaviour
{
    public Color32[] sourceColors;
    public Color32[] targetColors;
    public byte[] sourceBytes;
    public byte[] targetBytes;
    public int sourceOffset;
    public int targetOffset;
    public int count;
    public Texture sourceTexture;
    public Texture2D uploadTexture;
    public bool completed;
    public bool bytesValid;
    public bool colorsValid;

    public void Control() { }
    public void LoopColors() { EncoderUdonTextureRuntime.CopyPixelBuffer(sourceColors, targetColors); }
    public void BulkColors() { Array.Copy(sourceColors, 0, targetColors, 0, count); }
    public void LoopBytes() { NetworkValueWriter.WriteRawBytes(targetBytes, targetOffset, sourceBytes); }
    public void BulkBytes() { Array.Copy(sourceBytes, sourceOffset, targetBytes, targetOffset, count); }
    public void UploadBytes()
    {
        uploadTexture.LoadRawTextureData(sourceBytes);
        uploadTexture.Apply(false, false);
    }

#if UDONSHARP || COMPILER_UDONSHARP
    public void RequestBytes()
    {
        completed = false;
        VRCAsyncGPUReadback.Request(sourceTexture, 0, this);
    }

    public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
    {
        bytesValid = !request.hasError && request.TryGetData(targetBytes);
        colorsValid = !request.hasError && request.TryGetData(targetColors);
        completed = true;
    }
#endif
}
