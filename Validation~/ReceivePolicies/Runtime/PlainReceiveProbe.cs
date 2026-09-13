using K13A.TSMP;

public sealed class PlainReceiveProbe : TSMPBehaviour
{
    public int value;
    public uint lastVariableHash;
    public int notifications;

    public void OnTSMPVariableReceived()
    {
        notifications++;
    }
}
