using UnityEngine;

namespace K13A.TSMP
{
    public static class DecoderBindingRuntime
    {
        public static int ComputeBindingLookupSignature(int targetCount, ushort[] bindingNetworkIds, uint[] bindingVariableHashes)
        {
            return BindingLookup.ComputeSignature(targetCount, bindingNetworkIds, bindingVariableHashes);
        }

        public static bool IsArrayValueCacheValid(
            int targetCount,
            byte[][] rawByteValueArrays,
            bool[][] boolValueArrays,
            int[][] intValueArrays,
            float[][] floatValueArrays,
            Vector2[][] vector2ValueArrays,
            Vector3[][] vector3ValueArrays,
            Quaternion[][] quaternionValueArrays,
            string[][] stringValueArrays)
        {
            return rawByteValueArrays != null && rawByteValueArrays.Length == targetCount
                && boolValueArrays != null && boolValueArrays.Length == targetCount
                && intValueArrays != null && intValueArrays.Length == targetCount
                && floatValueArrays != null && floatValueArrays.Length == targetCount
                && vector2ValueArrays != null && vector2ValueArrays.Length == targetCount
                && vector3ValueArrays != null && vector3ValueArrays.Length == targetCount
                && quaternionValueArrays != null && quaternionValueArrays.Length == targetCount
                && stringValueArrays != null && stringValueArrays.Length == targetCount;
        }

        public static void EnsureBindingLookup(
            int targetCount,
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            ushort[] bindingLookupNetworkIds,
            uint[] bindingLookupVariableHashes,
            int[] bindingLookupBindingIndices,
            int bindingLookupCount,
            int bindingLookupSignature,
            out ushort[] nextBindingLookupNetworkIds,
            out uint[] nextBindingLookupVariableHashes,
            out int[] nextBindingLookupBindingIndices,
            out int nextBindingLookupCount,
            out int nextBindingLookupSignature)
        {
            int signature = ComputeBindingLookupSignature(targetCount, bindingNetworkIds, bindingVariableHashes);
            int count = BindingTable.ClampNetworkHashCount(targetCount, bindingNetworkIds, bindingVariableHashes);

            nextBindingLookupNetworkIds = bindingLookupNetworkIds;
            nextBindingLookupVariableHashes = bindingLookupVariableHashes;
            nextBindingLookupBindingIndices = bindingLookupBindingIndices;
            nextBindingLookupCount = bindingLookupCount;
            nextBindingLookupSignature = bindingLookupSignature;

            if (BindingLookup.IsCacheValid(bindingLookupNetworkIds, bindingLookupVariableHashes, bindingLookupBindingIndices, bindingLookupCount, bindingLookupSignature, count, signature))
                return;

            nextBindingLookupNetworkIds = new ushort[count];
            nextBindingLookupVariableHashes = new uint[count];
            nextBindingLookupBindingIndices = new int[count];
            nextBindingLookupCount = count;
            nextBindingLookupSignature = signature;

            BindingLookup.Build(nextBindingLookupNetworkIds, nextBindingLookupVariableHashes, nextBindingLookupBindingIndices, count, bindingNetworkIds, bindingVariableHashes);
        }
    }
}
