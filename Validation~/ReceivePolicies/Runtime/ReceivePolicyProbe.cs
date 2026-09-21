using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

public sealed class ReceivePolicyProbe : TSMPNetworkBehaviour
{
    [TransSync] public int value;
    public string text;
    public byte[] bytes;
    public bool[] bools;
    public int[] ints;
    public float[] floats;
    public Vector2[] vectors2;
    public Vector3[] vectors3;
    public Quaternion[] rotations;
    public string[] strings;
    public int notifications;
    public byte[] payload;
    public int valueType;
    public string fieldName;
    public int appliedCount;
    public int rejectedCount;
#if UDONSHARP || COMPILER_UDONSHARP
    public UdonBehaviour receiver;
#else
    public Component receiver;
#endif
    private byte[][] _bytes = new byte[1][];
    private bool[][] _bools = new bool[1][];
    private int[][] _ints = new int[1][];
    private float[][] _floats = new float[1][];
    private Vector2[][] _vectors2 = new Vector2[1][];
    private Vector3[][] _vectors3 = new Vector3[1][];
    private Quaternion[][] _rotations = new Quaternion[1][];
    private string[][] _strings = new string[1][];

    public void ApplyReceivedValue()
    {
        appliedCount = DecoderVariableRuntime.ApplyVariableValue(payload, 1, 100u, valueType, 0, payload.Length, 1,
#if UDONSHARP || COMPILER_UDONSHARP
            new UdonBehaviour[] { receiver },
#else
            new Component[] { receiver },
#endif
            new ushort[] { 1 }, new uint[] { 100 }, new byte[] { (byte)valueType }, new string[] { fieldName },
            new ushort[] { 1 }, new uint[] { 100 }, new int[] { 0 }, 1,
            _bytes, _bools, _ints, _floats, _vectors2, _vectors3, _rotations, _strings, out rejectedCount);
    }

    public override void OnTSMPVariableReceived()
    {
        notifications++;
    }
}
