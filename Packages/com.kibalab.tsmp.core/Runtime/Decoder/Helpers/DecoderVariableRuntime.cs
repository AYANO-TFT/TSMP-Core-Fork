using UnityEngine;

#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

namespace K13A.TSMP
{
    public static class DecoderVariableRuntime
    {
        public static int ApplyVariableValue(
            byte[] payloadBytes,
            ushort networkId,
            uint variableHash,
            int valueType,
            int valueOffset,
            int valueLength,
            int bindingCount,
#if UDONSHARP || COMPILER_UDONSHARP
            UdonBehaviour[] targets,
#else
            Component[] targets,
#endif
            ushort[] bindingNetworkIds,
            uint[] bindingVariableHashes,
            byte[] bindingValueTypes,
            string[] bindingFieldNames,
            ushort[] bindingLookupNetworkIds,
            uint[] bindingLookupVariableHashes,
            int[] bindingLookupBindingIndices,
            int bindingLookupCount,
            byte[][] rawByteValueArrays,
            bool[][] boolValueArrays,
            int[][] intValueArrays,
            float[][] floatValueArrays,
            Vector2[][] vector2ValueArrays,
            Vector3[][] vector3ValueArrays,
            Quaternion[][] quaternionValueArrays,
            string[][] stringValueArrays,
            out int rejectedValueTypeCount)
        {
            rejectedValueTypeCount = 0;

            int lookupStart = BindingLookup.FindStart(bindingLookupNetworkIds, bindingLookupVariableHashes, bindingLookupCount, networkId, variableHash);
            if (lookupStart < 0)
                return 0;

            int appliedCount = 0;
            for (int lookup = lookupStart; lookup < bindingLookupCount; lookup++)
            {
                if (!BindingLookup.LookupMatches(bindingLookupNetworkIds, bindingLookupVariableHashes, bindingLookupCount, lookup, networkId, variableHash))
                    break;

                int bindingIndex;
                string fieldName;
                if (!BindingLookup.TryResolveMatchingBinding(
                        bindingLookupNetworkIds,
                        bindingLookupVariableHashes,
                        bindingLookupBindingIndices,
                        bindingLookupCount,
                        bindingNetworkIds,
                        bindingVariableHashes,
                        bindingFieldNames,
                        bindingCount,
                        lookup,
                        networkId,
                        variableHash,
                        out bindingIndex,
                        out fieldName))
                    continue;

                if (!BindingTable.IsExpectedValueType(bindingValueTypes, bindingIndex, valueType))
                {
                    rejectedValueTypeCount++;
                    continue;
                }

                if (!DecoderVariableDispatcher.IsTargetActive(targets, bindingIndex))
                    continue;

                object decodedValue = DecoderValueRuntime.DecodeProgramVariableValue(
                    payloadBytes,
                    bindingIndex,
                    valueType,
                    valueOffset,
                    valueLength,
                    rawByteValueArrays,
                    boolValueArrays,
                    intValueArrays,
                    floatValueArrays,
                    vector2ValueArrays,
                    vector3ValueArrays,
                    quaternionValueArrays,
                    stringValueArrays);

                if (DecoderVariableDispatcher.Apply(targets, bindingIndex, fieldName, variableHash, decodedValue))
                    appliedCount++;
            }

            return appliedCount;
        }
    }
}
