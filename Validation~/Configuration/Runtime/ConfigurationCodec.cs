using K13A.TSMP;

public sealed class ConfigurationCodec : TSMPCodec
{
    public int option;

    public override int GetEncoderPayloadStartRow(int width, int blockSize)
    {
        return 5 + option;
    }

    public override int GetEncoderPayloadCapacityBytes(int width, int height, int blockSize)
    {
        return 1024 + option;
    }

    public override int GetEncoderCodecOptionByteCount()
    {
        return 1;
    }

    public override int GetEncoderCodecOptionByte(int index)
    {
        return index == 0 ? option : 0;
    }
}
