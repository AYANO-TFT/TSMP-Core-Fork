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
