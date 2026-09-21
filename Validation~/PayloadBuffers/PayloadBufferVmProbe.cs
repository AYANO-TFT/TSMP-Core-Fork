using K13A.TSMP;

public sealed class PayloadBufferVmProbe : TSMPBehaviour
{
    public byte[] buffer;
    public int byteCount;
    public byte[] first;
    public byte[] second;
    public int fieldIndex;

    public void EnsureCapacity()
    {
        buffer = DecoderReadbackRuntime.EnsureByteBuffer(buffer, byteCount);
    }

    public void CopyField()
    {
        if (fieldIndex == 0)
            first = NetworkValueReader.CopyRawBytes(buffer, 0, byteCount, first);
        else
            second = NetworkValueReader.CopyRawBytes(buffer, 0, byteCount, second);
    }
}
