---
title: TSMPNetworkBehaviour
---

# TSMPNetworkBehaviour

Namespace: `K13A.TSMP.Udon`

Base type for TSMP-synchronized behaviours.

Use this when a component should participate in TSMP message routing. It provides a network ID, receive interpolation policy, lifecycle hooks, and `SendTransRPC`.

## Fields

| Field | Type | Use |
| --- | --- | --- |
| `networkId` | `ushort` | Routes payload messages to this behaviour. |
| `sendMode` | `SendMode` | Component-wide override of automatic fields' change filtering. Defaults to `Default`. |
| `receiveInterpolation` | `ReceiveInterpolationMode` | Controls receive behaviour. |
| `continuousInterpolationRate` | `float` | Smoothing rate for continuous receive mode. |
| `transRpcEncoder` | `TSMPEncoder` | Encoder used by `SendTransRPC`. Assigned by setup. |
| `lastVariableHash` | `uint` | Last received variable hash. |
| `lastRpcHash` | `uint` | Last received RPC hash. |
| `lastRpcNetworkId` | `ushort` | Network ID for the last received RPC. |
| `lastRpcArgumentCount` | `int` | Argument count for the last received RPC. |
| `lastRpcMethodName` | `string` | Method name for the last received RPC. |

Do not assign `transRpcEncoder` manually in normal scenes. Let `TSMPSetup` assign it.

## SendMode

Namespace: `K13A.TSMP.Udon`. Inspector label: **Send Mode**.

| Value | Numeric value | Effective `SendOnChange` |
| --- | --- | --- |
| `Default` | `0` | Each field's attribute setting. |
| `OnChange` | `1` | `true` for every automatic field on this component. |
| `Always` | `2` | `false` for every automatic field on this component. |

```csharp
sendMode = SendMode.Always;
```

Changes are read during encoding, without rebuilding bindings. The existing field snapshots and successful-output times remain intact, so switching modes does not bypass `MinSendInterval`. Unknown enum values fall back to `Default`. Existing scenes and prefabs without this field use `Default`.

This override does not modify attribute metadata, priority, direction, capture logic, RPC behavior or manual Writer calls. `OnChange` retains the Encoder's periodic refresh. `Always` remains subject to minimum intervals and payload capacity. See [Send Mode](../components/network-behaviour.md#send-mode) for usage examples.

## ReceiveInterpolationMode

| Value | Meaning |
| --- | --- |
| `None` | Ignore received values. |
| `Discrete` | Apply received values directly. |
| `Continuous` | Store targets and interpolate where supported. |

Built-in components decide how to use `Continuous`. Custom components should check `receiveInterpolation` before applying received data.

## Lifecycle methods

| Method | Called when |
| --- | --- |
| `TSMPBeforeEncode()` | Before encoder reads `[TransSync]` fields. |
| `OnTSMPVariableReceived()` | After decoder applies a synchronized field. |
| `OnTSMPVariableChanged(uint variableHash)` | From the base receive handler. |
| `OnTSMPRpcReceived()` | After decoder dispatches a TSMP RPC. |
| `OnTSMPRpc(uint rpcHash)` | From the base RPC receive handler. |

Typical custom component flow:

```csharp
public override void TSMPBeforeEncode()
{
    packedBytes = BuildPacket();
}

public override void OnTSMPVariableReceived()
{
    if (receiveInterpolation == ReceiveInterpolationMode.None)
        return;

    ApplyPacket(packedBytes);
    OnTSMPVariableChanged(lastVariableHash);
}
```

## Utility methods

| Method | Use |
| --- | --- |
| `IsTSMPActive()` | Returns enabled and active-in-hierarchy state. |
| `GetReceiveInterpolationStep()` | Returns a clamped `0..1` interpolation step. |
| `SendTransRPC(string methodName, RPCTarget target)` | Queue an RPC event through TSMP. |

## RPCTarget

| Value | Behaviour |
| --- | --- |
| `Local` | Execute locally only. |
| `Remote` | Queue for receivers only. |
| `All` | Execute locally and queue for receivers. |

`SendTransRPC` hashes the method name and queues it through the assigned encoder. The receiving side dispatches by method name after decode.

## Example RPC

```csharp
public override void Interact()
{
    SendTransRPC(nameof(ToggleObject), RPCTarget.All);
}

public void ToggleObject()
{
    target.SetActive(!target.activeSelf);
}
```
