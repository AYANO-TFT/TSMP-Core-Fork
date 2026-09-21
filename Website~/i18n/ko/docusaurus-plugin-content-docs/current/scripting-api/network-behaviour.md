---
title: TSMPNetworkBehaviour
---

# TSMPNetworkBehaviour

Namespace: `K13A.TSMP.Udon`

TSMP-synchronized behaviour의 base type입니다.

Component가 TSMP message routing에 참여해야 할 때 사용합니다. Network ID, receive interpolation policy, lifecycle hooks, `SendTransRPC`를 제공합니다.

## Fields

| Field | Type | 용도 |
| --- | --- | --- |
| `networkId` | `ushort` | Payload message를 이 behaviour로 route합니다. |
| `sendMode` | `SendMode` | 자동 필드의 변경 감지 정책을 컴포넌트 단위로 재정의합니다. 기본값은 `Default`입니다. |
| `receiveInterpolation` | `ReceiveInterpolationMode` | Receive behaviour를 제어합니다. |
| `continuousInterpolationRate` | `float` | Continuous receive mode의 smoothing rate. |
| `transRpcEncoder` | `TSMPEncoder` | `SendTransRPC`가 사용하는 encoder. Setup이 할당합니다. |
| `lastVariableHash` | `uint` | 마지막으로 수신한 variable hash. |
| `lastRpcHash` | `uint` | 마지막으로 수신한 RPC hash. |
| `lastRpcNetworkId` | `ushort` | 마지막 RPC의 network ID. |
| `lastRpcArgumentCount` | `int` | 마지막 RPC argument count. |
| `lastRpcMethodName` | `string` | 마지막 RPC method name. |

일반 씬에서는 `transRpcEncoder`를 직접 할당하지 마세요. `TSMPSetup`에 맡깁니다.

## SendMode

네임스페이스: `K13A.TSMP.Udon`. 인스펙터 이름: **Send Mode**.

| 값 | 숫자 값 | 실제 적용되는 `SendOnChange` |
| --- | --- | --- |
| `Default` | `0` | 각 필드의 어트리뷰트 설정을 따릅니다. |
| `OnChange` | `1` | 이 컴포넌트의 자동 필드 전체에 `true`를 적용합니다. |
| `Always` | `2` | 이 컴포넌트의 자동 필드 전체에 `false`를 적용합니다. |

```csharp
sendMode = SendMode.Always;
```

인코딩 중에 값을 읽으므로 바인딩을 다시 만들 필요가 없습니다. 기존 필드 스냅샷과 출력 성공 시각을 유지하므로 모드를 전환해도 `MinSendInterval`을 우회하지 않습니다. 알 수 없는 enum 값은 `Default`로 처리합니다. 이 필드가 없던 기존 씬과 프리팹도 `Default`를 사용합니다.

어트리뷰트 메타데이터, 우선순위, 송수신 방향, 캡처 로직, RPC, 수동 Writer 호출은 변경하지 않습니다. `OnChange`에서도 인코더의 주기적 재전송이 적용되며, `Always`도 최소 간격과 용량 제한을 지킵니다. 사용 예시는 [Send Mode](../components/network-behaviour.md#send-mode)를 참고하세요.

## ReceiveInterpolationMode

| Value | 의미 |
| --- | --- |
| `None` | 수신 값을 무시합니다. |
| `Discrete` | 수신 값을 바로 적용합니다. |
| `Continuous` | Target을 저장하고 지원되는 곳에서 보간합니다. |

Built-in component는 `Continuous`를 어떻게 사용할지 직접 결정합니다. Custom component는 수신 데이터를 적용하기 전에 `receiveInterpolation`을 확인해야 합니다.

## Lifecycle methods

| Method | 호출 시점 |
| --- | --- |
| `TSMPBeforeEncode()` | Encoder가 `[TransSync]` fields를 읽기 전. |
| `OnTSMPVariableReceived()` | Decoder가 synchronized field를 적용한 뒤. |
| `OnTSMPVariableChanged(uint variableHash)` | Base receive handler에서 호출. |
| `OnTSMPRpcReceived()` | Decoder가 TSMP RPC를 dispatch한 뒤. |
| `OnTSMPRpc(uint rpcHash)` | Base RPC receive handler에서 호출. |

일반적인 custom component flow:

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

| Method | 용도 |
| --- | --- |
| `IsTSMPActive()` | Enabled 및 active-in-hierarchy state를 반환합니다. |
| `GetReceiveInterpolationStep()` | Clamped `0..1` interpolation step을 반환합니다. |
| `SendTransRPC(string methodName, RPCTarget target)` | TSMP를 통해 RPC event를 queue합니다. |

## RPCTarget

| Value | 동작 |
| --- | --- |
| `Local` | Local에서만 실행합니다. |
| `Remote` | Receiver용으로만 queue합니다. |
| `All` | Local에서 실행하고 receiver용으로 queue합니다. |

`SendTransRPC`는 method name을 hash하고 할당된 encoder를 통해 queue합니다. Receiver는 decode 후 method name으로 dispatch합니다.

## RPC 예제

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
