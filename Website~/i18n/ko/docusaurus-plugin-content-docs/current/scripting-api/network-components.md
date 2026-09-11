---
title: Built-in Network Components API
---

# Built-in network components

TSMP는 VRChat world에서 자주 쓰는 synchronization case를 위한 여러 `TSMPNetworkBehaviour` component를 제공합니다. 모두 `networkId`, receive interpolation, active-state check, setup binding을 공유합니다.

## Common behaviour

| Feature | 의미 |
| --- | --- |
| `networkId` | Sender와 receiver component를 matching하는 stable ID. |
| `receiveInterpolation` | `None`, `Discrete`, `Continuous` received-value handling. |
| Active state | Disabled component 또는 inactive GameObject는 참여하지 않습니다. |
| `TSMPBeforeEncode()` | Encoder가 `[TransSync]` field를 읽기 전에 state를 capture합니다. |
| `OnTSMPVariableReceived()` | Frame receive 후 decoded state를 적용합니다. |

## Component overview

| Component | 동기화 대상 |
| --- | --- |
| `TSMPNetworkTransformSync` | Transform position, rotation, scale, compression range, optional Rigidbody state. |
| `TSMPNetworkHumanoidPoseSync` | 선택한 humanoid bones와 root motion position. |
| `TSMPNetworkBlendShapesSync` | 하나의 `SkinnedMeshRenderer`에 대한 선택 blendshape weights. |
| `TSMPNetworkVrchatAvatarPoseSync` | Avatar-pool playback용 VRChat player tracking pose. |
| `TSMPNetworkAnimatorSync` | Runtime path가 지원하는 Animator parameters와 layer-related state. |
| `TSMPNetworkTimelineSync` | Udon에서 지원되는 PlayableDirector timeline time/play state. |
| `TSMPNetworkGameObjectToggle` | TSMP RPC를 통한 toggle event. |
| `TSMPDebugCanvas` | Frame counters, bitrate, loss, header metadata 표시용 text UI. |

## Timeline 제어 {#timeline-controls}

`TSMPNetworkTimelineSync`는 인자 없는 메서드 네 개를 제공합니다. Udon custom event로도 호출할 수 있습니다.

| 메서드 | 동작 |
| --- | --- |
| `Play()` | Director를 재생하고 Playing을 기록합니다. |
| `Pause()` | Director를 일시 정지하고 Paused를 기록합니다. 재생 시간이 0이어도 같습니다. |
| `Resume()` | Director의 재생을 재개하고 Playing을 기록합니다. |
| `Stop()` | Director를 정지하고 Stopped를 기록합니다. |

명령은 로컬 재생을 제어하며, Encoder가 다음 캡처 때 해당 상태를 전송합니다. 일반 Unity에서는 `director.state`와 graph 유효성을 읽으므로 외부 스크립트의 제어도 감지합니다. Udon에서는 명령을 추적하므로 이 컴포넌트를 통해 제어하고, 시퀀스 종료 시에도 명시적으로 명령을 호출해야 합니다. Director 직접 제어와 자동 종료는 정확히 감지할 수 없습니다. Director 교체 시 추적 상태는 새 Director의 `playOnAwake`를 기준으로 초기화됩니다.

## Choosing a component

Data model이 맞으면 built-in component를 사용하세요. 다음 경우 custom `TSMPNetworkBehaviour`를 작성합니다.

- 다른 packed byte format이 필요함.
- 여러 field를 하나의 packet으로 동기화해야 함.
- Custom receive interpolation policy가 필요함.
- TSMP RPC로 logic을 실행해야 함.

## Binding rule

Network component를 추가하거나 제거한 뒤 `Apply Setup`을 실행하세요. Encoder와 decoder는 uploaded world에서 매 frame field를 discover하지 않고, performance와 Udon compatibility를 위해 explicit binding table을 사용합니다.
