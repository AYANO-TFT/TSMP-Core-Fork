---
title: TSMPNetworkBehaviour
---

# TSMPNetworkBehaviour

`TSMPNetworkBehaviour`는 TSMP가 보내거나 받을 수 있는 데이터의 기본 컴포넌트입니다.

대부분의 사용자는 [Sync components](./sync-components.md)에 있는 기본 TSMP 네트워크 컴포넌트를 사용합니다. 각 컴포넌트에 공통으로 보이는 설정만 이해하면 됩니다.

## Network ID

각 동기화 컴포넌트에는 network ID가 있습니다. Sender와 receiver는 이 ID로 message를 object에 매칭합니다.

ID 할당과 갱신은 `TSMPSetup`을 사용하세요. 고정 mapping이 꼭 필요할 때만 수동으로 수정합니다.

규칙:

- 같은 stream을 공유하는 active TSMP network behaviour 사이에서 ID는 고유해야 합니다.
- Sender와 receiver가 같은 ID를 사용해야 합니다.
- Component를 추가, 삭제, 이동한 뒤에는 `Apply Setup`을 다시 실행해야 합니다.
- 두 behaviour가 같은 network ID를 공유하면 receiver가 잘못된 object에 데이터를 적용하거나 message를 무시할 수 있습니다.

Manual ID는 고급 옵션으로 취급하세요. Inspector는 실수로 바꾸는 것을 막기 위해 기본적으로 필드를 잠급니다.

## Send Mode

**송신 컴포넌트**의 Network ID 바로 아래에 있는 **Send Mode**에서 송신 방식을 선택하세요. 스크립트를 수정하거나 다른 컴포넌트까지 영향을 받는 인코더의 재전송 간격을 바꿀 필요가 없습니다.

| 모드 | 송신 방식 |
| --- | --- |
| Default | 각 필드의 `TransSync.SendOnChange` 설정을 따릅니다. 기본값이며 기존 동작을 유지합니다. |
| On Change | 최초 값, 변경된 값, 주기적인 재전송 값을 보냅니다. 작성자가 변경 감지를 꺼둔 필드에도 적용됩니다. |
| Always | 값이 그대로여도 매 인코딩마다 송신 대상으로 고려합니다. 정지한 Transform이나 일시 정지한 Timeline의 상태를 더 자주 반복해서 보낼 때 사용하세요. |

`On Change`는 인코더의 **Trans Sync Refresh Interval**을 따릅니다(기본 1초). 이 간격이 `0`이면 변경되지 않은 값의 주기적 재전송을 끕니다. `Always`는 재전송 간격을 기다리지 않지만, 필드의 최소 전송 간격, 활성 상태, 송수신 방향, 우선순위와 페이로드 용량 제한은 계속 지킵니다. 전송량이 늘어나며, 도착을 보장하거나 우선순위를 자동으로 높이는 옵션은 아닙니다.

이 설정은 해당 컴포넌트의 자동 `[TransSync]` 필드 전체에 적용됩니다. 다른 컴포넌트, RPC, 수동 Writer 호출에는 영향을 주지 않으며 컴포넌트 내부의 캡처·패킹 방식도 바꾸지 않습니다. 필드마다 다르게 설정하려면 `Default`를 선택하고 [필드 어트리뷰트](../scripting-api/transsync.md)를 사용하세요.

Send Mode만 변경할 때는 `Apply Setup` 없이 다음 전송 가능한 인코딩부터 반영됩니다. 일반 Unity와 UdonSharp 양쪽에서 동작합니다. 수신 컴포넌트의 **Receive Interpolation**은 별도 설정입니다.

## Receive interpolation

컴포넌트별 receive interpolation을 설정합니다.

| Mode | 사용 시점 |
| --- | --- |
| None | 이 object가 수신 값을 적용하면 안 되는 경우. sender-only object에 유용합니다. |
| Discrete | 최신 수신 상태로 즉시 snap해야 하는 경우. event, toggle, animator state, 낮은 빈도의 값에 적합합니다. |
| Continuous | 수신 상태 사이를 부드럽게 이동해야 하는 경우. transform과 pose target에 적합합니다. |

`None`은 송신을 막지 않습니다. 해당 컴포넌트에서 들어오는 값을 무시한다는 뜻입니다.

`None`에서는 수신한 TransSync 값이 필드나 이전에 수신한 배열의 내용을 덮어쓰지 않습니다. 디코더는 `lastVariableHash`도 변경하지 않으며 `OnTSMPVariableReceived`를 호출하지 않습니다. `Discrete` 또는 `Continuous`로 되돌리면 이후 수신값부터 다시 반영됩니다. 이 설정은 RPC 수신을 차단하지 않습니다.

## Active state

TSMP는 component와 GameObject active state를 기본 on/off switch로 사용합니다.

- Component를 비활성화하면 해당 behaviour가 참여하지 않습니다.
- GameObject를 비활성화하면 그 위의 모든 TSMP behaviour가 멈춥니다.
- 동기화 object를 추가, 제거, 구조적으로 이동한 경우 setup을 다시 실행하세요.

일시적인 런타임 enable/disable은 encode와 receive 중에 반영됩니다. 영구적인 씬 구조 변경은 `Apply Setup`을 다시 실행해야 합니다.

## RPC 보내기

`TSMPNetworkBehaviour`에서 `SendTransRPC`를 사용합니다.

```csharp
SendTransRPC(nameof(ToggleObject), RPCTarget.All);
```

Target:

| Target | 동작 |
| --- | --- |
| `Local` | 호출자만 method를 실행합니다. TSMP로 보내지 않습니다. |
| `Remote` | 수신자만 stream delay 뒤 method를 실행합니다. |
| `All` | 호출자는 즉시 실행하고, 수신자는 stream delay 뒤 실행합니다. |

최소 예제는 `TSMPNetworkGameObjectToggle`을 사용하세요.

## Receiver object가 업데이트되지 않을 때

확인할 것:

1. Receiver component가 enabled이고 active인지.
2. Receive interpolation이 `None`이 아닌지.
3. Network ID가 sender와 일치하는지.
4. Component 추가 후 `TSMPSetup`을 적용했는지.
5. Decoder message count가 0보다 큰지.
