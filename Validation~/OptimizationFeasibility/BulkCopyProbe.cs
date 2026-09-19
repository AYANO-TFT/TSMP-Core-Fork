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
    public int writeResult;
    public Texture sourceTexture;
    public Texture2D uploadTexture;
    public bool completed;
    public bool bytesValid;
    public bool colorsValid;
    public RenderTexture rasterOutput;
    public Material rasterMaterial;
    public Material expandMaterial;
    public Texture2D rasterTexture;
    public byte[] rasterHeader;
    public Color32[] rasterBase;
    public Color32[] rasterPixels;
    public Color32[] lumaColors;
    public bool rasterResult;
    private Texture2D gpuUpload;
    private byte[] gpuBytes;
    private RenderTexture gpuSymbols;

    public void CpuRaster()
    {
        int width=rasterOutput.width;
        int height=rasterOutput.height;
        EncoderUdonTextureRuntime.CopyPixelBuffer(rasterBase,rasterPixels);
        Luma4FrameTextureWriter.WriteHeader(rasterPixels,width,height,8,width/8,height/8,true,rasterHeader,lumaColors);
        Luma4FrameTextureWriter.WritePayload(rasterPixels,width,height,8,width/8,height/8,true,5,sourceBytes,count,lumaColors);
        rasterTexture.SetPixels32(rasterPixels);
        rasterTexture.Apply(false,false);
        EncoderUdonTextureRuntime.BlitEncodedTexture(rasterTexture,rasterOutput,true,expandMaterial,width/8,height/8,8);
    }

    public void GpuRaster()
    {
        rasterResult=GpuLuma4Writer.TryWrite(rasterOutput,rasterMaterial,8,5,rasterHeader,sourceBytes,count,0f,ref gpuUpload,ref gpuBytes,ref gpuSymbols);
    }

    public void ReleaseRaster()
    {
        GpuLuma4Writer.ReleaseTexture(gpuUpload);
        DecoderSnapshotRuntime.Release(gpuSymbols);
        gpuUpload=null;
        gpuBytes=null;
        gpuSymbols=null;
    }

    public void Control() { }
    public void LoopColors() { EncoderUdonTextureRuntime.CopyPixelBuffer(sourceColors, targetColors); }
    public void BulkColors() { Array.Copy(sourceColors, 0, targetColors, 0, count); }
    public void LoopBytes() { writeResult = NetworkValueWriter.WriteRawBytes(targetBytes, targetOffset, sourceBytes); }
    public void ReadBytes() { targetBytes = NetworkValueReader.CopyRawBytes(sourceBytes, sourceOffset, count, targetBytes); }
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
