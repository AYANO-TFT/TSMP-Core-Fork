using UnityEngine;

namespace K13A.TSMP
{
    public static class DecoderValueRuntime
    {
        public static object DecodeProgramVariableValue(
            byte[] payloadBytes,
            int bindingIndex,
            int valueType,
            int valueOffset,
            int valueLength,
            byte[][] rawByteValueArrays,
            bool[][] boolValueArrays,
            int[][] intValueArrays,
            float[][] floatValueArrays,
            Vector2[][] vector2ValueArrays,
            Vector3[][] vector3ValueArrays,
            Quaternion[][] quaternionValueArrays,
            string[][] stringValueArrays)
        {
            object decodedValue = NetworkValueReader.ReadScalarObject(payloadBytes, valueType, valueOffset, valueLength);
            if (decodedValue != null)
                return decodedValue;

            if (valueType == NetworkFrameProtocol.ValueTypeRawBytes)
                return DecoderValueCache.CopyRawBytes(payloadBytes, valueOffset, valueLength, rawByteValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeBoolArray)
                return DecoderValueCache.CopyBoolArray(payloadBytes, valueOffset, valueLength, boolValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeInt32Array)
                return DecoderValueCache.CopyInt32Array(payloadBytes, valueOffset, valueLength, intValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeFloat32Array)
                return DecoderValueCache.CopyFloat32Array(payloadBytes, valueOffset, valueLength, floatValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeVector2Array)
                return DecoderValueCache.CopyVector2Array(payloadBytes, valueOffset, valueLength, vector2ValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeVector3Array)
                return DecoderValueCache.CopyVector3Array(payloadBytes, valueOffset, valueLength, vector3ValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeQuaternionArray)
                return DecoderValueCache.CopyQuaternionArray(payloadBytes, valueOffset, valueLength, quaternionValueArrays, bindingIndex);
            if (valueType == NetworkFrameProtocol.ValueTypeUTF8StringArray)
                return DecoderValueCache.CopyStringArray(payloadBytes, valueOffset, valueLength, stringValueArrays, bindingIndex);

            return null;
        }
    }
}
