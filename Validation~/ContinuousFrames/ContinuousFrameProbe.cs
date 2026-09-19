using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public sealed class ContinuousFrameProbe : TSMPNetworkBehaviour
{
    [TransSync("continuous-frame", SendOnChange = false)]
    public byte[] packet;

    public int[] appliedIds;
    public float[] appliedTimes;
    public int appliedCount;
    public int corruptCount;
    public int rpcCount;

    public Color32[] profilePixels;
    public byte[] profileBuffer;
    public byte[] profileValue;
    public uint[] profileCrcTable;
    public uint profileResult;
    public Color32[] profileBasePixels;
    public Color32[] profileFramePixels;
    public Color32[] profileColors;
    public int profileWidthBlocks;
    public int profileHeightBlocks;

    public void ProfileBasePixelCopy()
    {
        EncoderUdonTextureRuntime.CopyPixelBuffer(profileBasePixels, profileFramePixels);
    }

    public void ProfileLumaPayload()
    {
        FrameRaster.WriteLuma4BytesToBlockTexture(profileFramePixels, profileWidthBlocks, profileHeightBlocks,
            profileWidthBlocks * 5, profileWidthBlocks * (profileHeightBlocks - 6), profileBuffer, 0, profileBuffer.Length, profileColors);
    }

    public void ProfileCopyPixels()
    {
        ByteTextureReader.CopyBytesAtPixel(profilePixels, 0, profileBuffer, profileBuffer.Length);
    }

    public void ProfileCopyRawBytes()
    {
        profileValue = NetworkValueReader.CopyRawBytes(profileBuffer, 0, profileBuffer.Length, profileValue);
    }

    public void ProfileWriteRawBytes()
    {
        NetworkValueWriter.WriteRawBytes(profileBuffer, 0, profileValue);
    }

    public void ProfileHeaderCrc()
    {
        profileResult = Crc32Runtime.Compute(profileBuffer, 0, FrameHeader.BytesBeforeCrc, profileCrcTable);
    }

    public void ProfileControl()
    {
        profileResult++;
    }

    public void ReceiveProbeRpc()
    {
        rpcCount++;
    }

    public void QueueProbeRpc()
    {
        SendTransRPC(nameof(ReceiveProbeRpc), RPCTarget.Remote);
    }

    public override void OnTSMPVariableReceived()
    {
        if (packet == null || packet.Length < 8 || appliedCount >= appliedIds.Length)
        {
            corruptCount++;
            return;
        }

        int id = (int)packet[0] | ((int)packet[1] << 8) | ((int)packet[2] << 16);
        int end = packet.Length - 4;
        if ((int)packet[end] != 255 - (int)packet[0] ||
            (int)packet[end + 1] != 255 - (int)packet[1] ||
            (int)packet[end + 2] != 255 - (int)packet[2])
            corruptCount++;
        appliedIds[appliedCount] = id;
        appliedTimes[appliedCount] = Time.realtimeSinceStartup;
        appliedCount++;
    }
}
