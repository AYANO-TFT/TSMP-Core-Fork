using K13A.TSMP;
using K13A.TSMP.Udon;

public class SendCommitProbe : TSMPNetworkBehaviour
{
    [TransSync("sent.bytes", SentEvent = nameof(CommitBytes))] public byte[] bytes = new byte[100];
    [TransSync("sent.other", SendOnChange = false)] public int other;
    public int commits;
    public int captures;
    public int committedValue;
    public bool reenter;

    public override void TSMPBeforeEncode() { captures++; }

    public void CommitBytes()
    {
        commits++;
        committedValue = bytes[0];
        if (reenter) transRpcEncoder.EncodeNow();
    }
}
