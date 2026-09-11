using K13A.TSMP;
using UnityEngine;

public sealed class DecoderArrayCacheVmProbe : TSMPBehaviour
{
    public byte[] payload;
    public int valueType;
    public int bindingIndex;
    public object decodedValue;

    private byte[][] _bytes;
    private bool[][] _bools;
    private int[][] _ints;
    private float[][] _floats;
    private Vector2[][] _vectors2;
    private Vector3[][] _vectors3;
    private Quaternion[][] _rotations;
    private string[][] _strings;

    public void DecodeValue()
    {
        if (!DecoderBindingRuntime.IsArrayValueCacheValid(2, _bytes, _bools, _ints, _floats, _vectors2, _vectors3, _rotations, _strings))
        {
            _bytes = new byte[2][];
            _bools = new bool[2][];
            _ints = new int[2][];
            _floats = new float[2][];
            _vectors2 = new Vector2[2][];
            _vectors3 = new Vector3[2][];
            _rotations = new Quaternion[2][];
            _strings = new string[2][];
        }

        decodedValue = DecoderValueRuntime.DecodeProgramVariableValue(
            payload, bindingIndex, valueType, 0, payload.Length,
            _bytes, _bools, _ints, _floats, _vectors2, _vectors3, _rotations, _strings);
    }
}
