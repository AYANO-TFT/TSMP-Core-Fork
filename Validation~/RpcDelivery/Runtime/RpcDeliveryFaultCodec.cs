#if !UDONSHARP && !COMPILER_UDONSHARP
using System;
using K13A.TSMP;
using UnityEngine;

public sealed class RpcDeliveryFaultCodec : TSMPCodec
{
    public TSMPCodec realCodec;
    public bool fail;
    public bool throwOnWrite;
    public int capacity = -1;
    public Action beforeWrite;

    public override int GetPayloadCapacityBytes(int width, int height, int blockSize)
    {
        return capacity < 0 ? realCodec.GetPayloadCapacityBytes(width, height, blockSize) : capacity;
    }

    public override bool TryWriteFrame(Texture2D texture, int blockSize, byte[] headerBytes, byte[] payloadBytes, out string error)
    {
        beforeWrite?.Invoke();
        if (throwOnWrite) throw new InvalidOperationException("Injected RPC codec exception");
        if (fail)
        {
            error = "Injected RPC codec failure";
            return false;
        }
        return realCodec.TryWriteFrame(texture, blockSize, headerBytes, payloadBytes, out error);
    }
}
#endif
