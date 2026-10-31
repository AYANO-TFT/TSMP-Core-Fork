using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
#if UDONSHARP || COMPILER_UDONSHARP
using VRC.Udon;
#endif

// Regression fixture. Import only into an existing validation project; never ship it.
public class DecoderDispatchProbe : TSMPNetworkBehaviour
{
#if UDONSHARP || COMPILER_UDONSHARP
    public UdonBehaviour targetA;
    public UdonBehaviour targetB;
    private UdonBehaviour[] _targets;
#else
    public Component targetA;
    public Component targetB;
    private Component[] _targets;
#endif
    public int receivedValue;
    public int callbackCount;
    public uint observedHash;
    public GameObject deactivateOnReceive;
    public int checks;
    public int failures;
    public string failureDetails;
    public int benchmarkIterations = 400;
    public int benchmarkApplied;
    public double benchmarkSeconds;
    private byte[] _payload;
    private ushort[] _ids;
    private uint[] _hashes;
    private byte[] _types;
    private string[] _fields;
    private int[] _indices;
    private int _rejected;

    public override void OnTSMPVariableReceived()
    {
        callbackCount++;
        observedHash = lastVariableHash;
        if (deactivateOnReceive != null)
            deactivateOnReceive.SetActive(false);
    }

    private void Prepare()
    {
#if UDONSHARP || COMPILER_UDONSHARP
        _targets = new UdonBehaviour[] { targetA, targetB };
#else
        _targets = new Component[] { targetA, targetB };
#endif
        _payload = new byte[] { 42, 0, 0, 0 };
        _ids = new ushort[] { 7, 7 };
        _hashes = new uint[] { 1234, 1234 };
        _types = new byte[] { NetworkFrameProtocol.ValueTypeInt32, NetworkFrameProtocol.ValueTypeInt32 };
        _fields = new string[] { nameof(receivedValue), nameof(receivedValue) };
        _indices = new int[] { 0, 1 };
    }

    private void ResetTargets()
    {
        _targets[0] = targetA;
        _targets[1] = targetB;
        for (int i = 0; i < 2; i++)
        {
            _targets[i].gameObject.SetActive(true);
            ((Behaviour)_targets[i]).enabled = true;
            TSMPBehaviour.SetProgramVariable(_targets[i], nameof(receiveInterpolation), ReceiveInterpolationMode.Discrete);
            TSMPBehaviour.SetProgramVariable(_targets[i], nameof(receivedValue), -1);
            TSMPBehaviour.SetProgramVariable(_targets[i], nameof(callbackCount), 0);
            TSMPBehaviour.SetProgramVariable(_targets[i], nameof(observedHash), (uint)0);
            TSMPBehaviour.SetProgramVariable(_targets[i], nameof(deactivateOnReceive), null);
        }
    }

    private int Apply(int count, ushort id, uint hash, int type, int length)
    {
        return DecoderVariableRuntime.ApplyVariableValue(_payload, id, hash, type, 0, length,
            count, _targets, _ids, _hashes, _types, _fields, _ids, _hashes, _indices, count,
            null, null, null, null, null, null, null, null, out _rejected);
    }

    private void Check(bool condition, string name)
    {
        checks++;
        if (condition) return;
        failures++;
        failureDetails += name + "\n";
    }

    private int Count(int index)
    {
        return (int)TSMPBehaviour.GetProgramVariable(_targets[index], nameof(callbackCount));
    }

    private bool Value(int index, int expected)
    {
        return (int)TSMPBehaviour.GetProgramVariable(_targets[index], nameof(receivedValue)) == expected;
    }

    public void RunChecks()
    {
        checks = 0; failures = 0; failureDetails = "";
        Prepare(); ResetTargets();
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 1, "active target applies once");
        Check(Count(0) == 1 && Count(1) == 0 && Value(0, 42), "value set and exactly one callback");
        Check((uint)TSMPBehaviour.GetProgramVariable(targetA, nameof(observedHash)) == 1234, "hash set before callback");

        ResetTargets(); ((Behaviour)targetA).enabled = false;
#if UDONSHARP || COMPILER_UDONSHARP
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 1 && Count(0) == 1 && Value(0, 42), "Udon preserves existing component-enabled semantics");
#else
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 0 && Count(0) == 0 && Value(0, -1), "disabled behaviour rejected");
#endif
        ResetTargets(); targetA.gameObject.SetActive(false);
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 0 && Count(0) == 0 && Value(0, -1), "inactive object rejected");
        ResetTargets(); TSMPBehaviour.SetProgramVariable(targetA, nameof(receiveInterpolation), ReceiveInterpolationMode.None);
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 0 && Count(0) == 0 && Value(0, -1), "receive None rejected");
        TSMPBehaviour.SetProgramVariable(targetA, nameof(receiveInterpolation), ReceiveInterpolationMode.Continuous);
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 1 && Count(0) == 1, "receive mode change observed next application");

        ResetTargets();
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeFloat32, 4) == 0 && _rejected == 1 && Count(0) == 0, "wrong type rejected");
        Check(Apply(1, 8, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 0 && Count(0) == 0, "unregistered network id rejected");
        Check(Apply(1, 7, 1235, NetworkFrameProtocol.ValueTypeInt32, 4) == 0 && Count(0) == 0, "unregistered hash rejected");
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 0) == 0 && Count(0) == 0, "null decoded value rejected");
        _targets[0] = null;
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 0, "missing recipient rejected");

        ResetTargets();
        Check(Apply(2, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 2 && Count(0) == 1 && Count(1) == 1, "fanout visits each recipient once");
        ResetTargets(); TSMPBehaviour.SetProgramVariable(targetA, nameof(deactivateOnReceive), targetB.gameObject);
        Check(Apply(2, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 1 && Count(0) == 1 && Count(1) == 0 && Value(1, -1), "callback disabling next recipient is respected");
        ResetTargets(); TSMPBehaviour.SetProgramVariable(targetA, nameof(deactivateOnReceive), targetA.gameObject);
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 1, "self-disabling callback completes once");
        Check(Apply(1, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4) == 0 && Count(0) == 1, "self-disabled target rejected next time");

        ResetTargets(); ((Behaviour)targetA).enabled = false;
#if UDONSHARP || COMPILER_UDONSHARP
        Check(DecoderVariableDispatcher.Apply(_targets, 0, nameof(receivedValue), 1234, 42) && Count(0) == 1, "direct Udon Apply preserves component-enabled semantics");
#else
        Check(!DecoderVariableDispatcher.Apply(_targets, 0, nameof(receivedValue), 1234, 42) && Count(0) == 0, "direct Apply retains active guard");
#endif
        ResetTargets();
        Check(!DecoderVariableDispatcher.Apply(_targets, -1, nameof(receivedValue), 1234, 42), "direct Apply rejects invalid index");
        Check(!DecoderVariableDispatcher.Apply(null, 0, nameof(receivedValue), 1234, 42), "direct Apply rejects missing array");
        Check(!DecoderVariableDispatcher.Apply(_targets, 0, nameof(receivedValue), 1234, null) && Count(0) == 0, "direct Apply rejects null value");
        Check(DecoderVariableDispatcher.Apply(_targets, 0, nameof(receivedValue), 1234, 42) && Count(0) == 1 && Value(0, 42), "direct Apply remains functional");
        ResetTargets();
    }

    public void RunBenchmark()
    {
        Prepare(); ResetTargets(); benchmarkApplied = 0;
        // Warm up the actual decoder path before measuring the batch.
        Apply(2, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4);
        double start = Time.realtimeSinceStartupAsDouble;
        for (int i = 0; i < benchmarkIterations; i++)
            benchmarkApplied += Apply(2, 7, 1234, NetworkFrameProtocol.ValueTypeInt32, 4);
        benchmarkSeconds = Time.realtimeSinceStartupAsDouble - start;
    }
}
