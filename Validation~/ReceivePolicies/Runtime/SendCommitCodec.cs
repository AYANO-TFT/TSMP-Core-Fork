using K13A.TSMP;
using UnityEngine;

public class SendCommitCodec : TSMPCodec
{
    public bool fail;

    public override int GetEncoderSymbolMode() { return 254; }

    public override bool WriteEncoderPayload(Color32[] pixels, int width, int height, int blockSize, byte[] bytes, int count)
    {
        if (fail) return false;
        Luma4FrameTextureWriter.WritePayload(pixels, width, height, blockSize, width / blockSize, height / blockSize,
            encoderPixelsAreBlocks, Luma4Raster.PayloadStartRow, bytes, count, SymbolCodec.CreateLuma4Colors());
        return true;
    }

#if !COMPILER_UDONSHARP
    public override bool TryWriteFrame(Texture2D texture, int blockSize, byte[] header, byte[] payload, out string error)
    {
        error = "Intentional codec failure";
        return !fail && Luma4Raster.TryWriteFrame(texture, blockSize, header, payload, out error);
    }
#endif
}
