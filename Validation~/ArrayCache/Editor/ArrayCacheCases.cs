using System;
using K13A.TSMP;
using UnityEngine;

public static class ArrayCacheCases
{
    public static readonly int[] Types =
    {
        NetworkFrameProtocol.ValueTypeRawBytes,
        NetworkFrameProtocol.ValueTypeBoolArray,
        NetworkFrameProtocol.ValueTypeInt32Array,
        NetworkFrameProtocol.ValueTypeFloat32Array,
        NetworkFrameProtocol.ValueTypeVector2Array,
        NetworkFrameProtocol.ValueTypeVector3Array,
        NetworkFrameProtocol.ValueTypeQuaternionArray,
        NetworkFrameProtocol.ValueTypeUTF8StringArray
    };

    public static Array CreateValue(int type, int count, int seed)
    {
        Array values;
        if (type == NetworkFrameProtocol.ValueTypeRawBytes) values = new byte[count];
        else if (type == NetworkFrameProtocol.ValueTypeBoolArray) values = new bool[count];
        else if (type == NetworkFrameProtocol.ValueTypeInt32Array) values = new int[count];
        else if (type == NetworkFrameProtocol.ValueTypeFloat32Array) values = new float[count];
        else if (type == NetworkFrameProtocol.ValueTypeVector2Array) values = new Vector2[count];
        else if (type == NetworkFrameProtocol.ValueTypeVector3Array) values = new Vector3[count];
        else if (type == NetworkFrameProtocol.ValueTypeQuaternionArray) values = new Quaternion[count];
        else if (type == NetworkFrameProtocol.ValueTypeUTF8StringArray) values = new string[count];
        else throw new ArgumentOutOfRangeException(nameof(type));

        for (int i = 0; i < count; i++)
        {
            int value = seed + i;
            if (values is byte[] bytes) bytes[i] = (byte)value;
            else if (values is bool[] booleans) booleans[i] = (value & 1) != 0;
            else if (values is int[] ints) ints[i] = value;
            else if (values is float[] floats) floats[i] = value + 0.25f;
            else if (values is Vector2[] vectors2) vectors2[i] = new Vector2(value, value + 0.5f);
            else if (values is Vector3[] vectors3) vectors3[i] = new Vector3(value, value + 0.5f, value + 1f);
            else if (values is Quaternion[] rotations) rotations[i] = new Quaternion(value, value + 0.5f, value + 1f, 1f);
            else if (values is string[] strings) strings[i] = "\uD83D\uDC41\uFE0F-" + value;
        }
        return values;
    }

    public static void Exercise(int type, Func<int, Array, Array> decode)
    {
        Array firstInput = CreateValue(type, 2, 1);
        Array secondInput = CreateValue(type, 2, 2);
        Array first = decode(0, firstInput);
        Array second = decode(1, secondInput);
        AssertEqual(firstInput, first);
        AssertEqual(secondInput, second);
        Require(!ReferenceEquals(first, second), "Different bindings share an array");
        Require(!ReferenceEquals(firstInput, first), "Decoder returned the input array");

        first.SetValue(CreateValue(type, 1, 9).GetValue(0), 0);
        AssertEqual(secondInput, second);
        Array nextFirstInput = CreateValue(type, 2, 3);
        Array nextFirst = decode(0, nextFirstInput);
        Require(ReferenceEquals(first, nextFirst), "Same-length update did not reuse its binding array");
        AssertEqual(nextFirstInput, nextFirst);
        AssertEqual(secondInput, second);

        Array nextSecondInput = CreateValue(type, 2, 4);
        Array nextSecond = decode(1, nextSecondInput);
        Require(ReferenceEquals(second, nextSecond), "Second binding did not reuse its array");
        AssertEqual(nextFirstInput, nextFirst);
        AssertEqual(nextSecondInput, nextSecond);

        foreach (int count in new[] { 0, 1, 3, 2 })
        {
            Array input = CreateValue(type, count, 5);
            Array result = decode(0, input);
            AssertEqual(input, result);
            Require(!ReferenceEquals(result, nextSecond), "Resized array aliases another binding");
            AssertEqual(nextSecondInput, nextSecond);
            Require(ReferenceEquals(result, decode(0, input)), "Repeated length allocated again");
        }
    }

    public static byte[] EncodeValue(int type, Array value)
    {
        var bytes = new byte[4096];
        int end = NetworkValueWriter.WriteObject(bytes, 0, type, value);
        Require(end >= 0, "Failed to encode test value");
        Array.Resize(ref bytes, end);
        return bytes;
    }

    public static void AssertEqual(Array expected, Array actual)
    {
        Require(actual != null && expected.GetType() == actual.GetType(), "Decoded array type differs");
        Require(expected.Length == actual.Length, "Decoded array length differs");
        for (int i = 0; i < expected.Length; i++)
            Require(Equals(expected.GetValue(i), actual.GetValue(i)), "Decoded element differs at " + i);
    }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
