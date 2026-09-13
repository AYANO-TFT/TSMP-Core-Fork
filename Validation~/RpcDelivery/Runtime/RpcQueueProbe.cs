using K13A.TSMP;
using K13A.TSMP.Udon;

public sealed class RpcQueueProbe : TSMPNetworkBehaviour
{
    [TransSync(SendOnChange = false)] public int value;
    public bool enqueueOnCapture;
    public bool accepted;
    public bool failFrame;
    public int captures;

    public override void TSMPBeforeEncode()
    {
        captures++;
        value++;
        if (enqueueOnCapture)
        {
            enqueueOnCapture = false;
            QueueEvent();
        }
#if UDONSHARP || COMPILER_UDONSHARP
        if (failFrame)
        {
            transRpcEncoder.BeginRpcCall(1, 99u);
            transRpcEncoder.WriteStringRpcArgument(new string('x', 2000));
            transRpcEncoder.EndRpcCall();
        }
#endif
    }

    public void QueueEvent()
    {
        accepted = SendTransRPC(nameof(ToggleObject), RPCTarget.Remote);
    }

    public void ToggleObject()
    {
    }

#if UDONSHARP || COMPILER_UDONSHARP
    public void WriteManualRpc()
    {
        transRpcEncoder.BeginFrame();
        transRpcEncoder.BeginRpcCall(1, 99u);
        transRpcEncoder.EndRpcCall();
    }
#endif
}
