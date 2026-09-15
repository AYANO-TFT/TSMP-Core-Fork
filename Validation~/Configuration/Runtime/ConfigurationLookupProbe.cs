using K13A.TSMP;

public sealed class ConfigurationLookupProbe : TSMPBehaviour
{
    public ushort[] ids;
    public uint[] hashes;
    public ushort[] lookupIds;
    public uint[] lookupHashes;
    public int[] lookupIndices;
    public int lookupCount;
    public int signature;

    public void Refresh()
    {
        DecoderBindingRuntime.EnsureBindingLookup(ids.Length, ids, hashes,
            lookupIds, lookupHashes, lookupIndices, lookupCount, signature,
            out lookupIds, out lookupHashes, out lookupIndices, out lookupCount, out signature);
    }
}
