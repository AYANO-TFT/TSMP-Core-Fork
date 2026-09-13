---
title: TSMPDecoder API
---

# `TSMPDecoder`

`TSMPDecoder`는 TSMP texture를 읽고 header를 검증한 뒤, 선택된 codec으로 payload bytes를 복구하고 variable/RPC message를 bound behaviour에 적용합니다.

Custom receiver wiring, diagnostics 확인, frame이 무시된 이유를 debug할 때 사용하세요.

## Component fields

| Field | 용도 |
| --- | --- |
| `sourceTexture` | Encoded TSMP frame이 들어있는 texture. |
| `payloadByteTexture` | Codec shader가 payload bytes를 복구할 때 사용하는 render texture. |
| `codecHandlers` | Decoder가 사용할 수 있는 codec components. Header의 `codecId`가 handler를 선택합니다. |
| `applyEveryFrame` | Component update loop에서 decode합니다. 다른 script가 `DecodeNow()`를 호출한다면 끄세요. |
| `skipDuplicateFrames` | Header frame index가 이미 처리된 frame이면 무시합니다. |
| `flipY` | Capture path가 image를 뒤집는 경우 texture sampling을 반전합니다. |
| `useHeaderPayloadLayout` | Header payload metadata로 readback size를 결정합니다. 일반적으로 켜둡니다. |
| `payloadBytesOverride` | Test path용 manual payload byte count. |
| `decodeSafetyMode` | Malformed 또는 partial frame에 대한 추가 guard. |

`TSMPSetup`이 보통 source texture, byte texture, codec handlers, binding arrays를 할당합니다.

## Header validation

Decoder는 다음 경우 payload decode 전에 frame을 버립니다.

- Magic bytes가 TSMP가 아님.
- Header size가 지원되지 않음.
- Major protocol version이 지원되지 않음.
- Header CRC 불일치.
- Payload size가 texture layout capacity를 초과.

CRC failure는 warning log로 표시됩니다. 손상된 capture frame이 잘못 적용되는 것을 막기 위한 동작입니다.

## `DecodeNow()`

```csharp
public void DecodeNow()
```

현재 이미지를 캡처하고 비동기 디코딩을 시작합니다. `applyEveryFrame`이 꺼져 있어도 호출할 수 있지만 컴포넌트는 활성화되어 있어야 합니다. readback이 대기 중이면 새 작업을 시작하지 않습니다.

실행 순서:

1. 현재 입력 이미지를 디코더의 스냅샷에 복사합니다.
2. 스냅샷에서 헤더 픽셀을 읽습니다.
3. Magic, version, payload layout, CRC를 검증합니다.
4. `codecId`로 codec handler를 선택합니다.
5. 같은 스냅샷에서 payload bytes를 디코딩합니다.
6. Network messages를 파싱합니다.
7. 변수를 적용하고 RPC를 호출합니다.

## 입력 스냅샷 {#input-snapshot}

Inspector에 추가 참조나 모드를 설정할 필요가 없습니다. 디코더가 입력과 같은 크기의 linear `ARGBFloat` RenderTexture를 소유하고, 작업 사이에 재사용하며 비활성화·제거 시 해제합니다. 원본을 샘플링한 값에 8비트·half-float 양자화 단계를 더하지 않기 위한 선택입니다. Float32 렌더 타깃 지원이 필요하며 포맷·할당에 실패하면 디코딩을 거부하고 기존 오류 로그 정책에 따라 `lastError`를 알립니다. 갱신 중인 원본을 대신 읽지는 않습니다.

추가 메모리는 `width * height * 16`바이트입니다. 640x360에서 약 3.52 MiB, 1920x1080에서 31.64 MiB, 3840x2160에서 126.56 MiB입니다. 시작한 디코딩 작업마다 입력 전체를 한 번 복사하며, 이후 CRC·중복 검사에서 거부되는 시도에도 이 비용이 듭니다. CPU readback은 추가하지 않습니다. 실제 배포 장치에서 GPU 복사 비용을 측정하세요.

캡처 이후 `sourceTexture`를 교체하거나 제거해도 현재 작업의 이미지는 바뀌지 않습니다. 변경은 다음 작업에 반영됩니다. 디코더를 비활성화하면 진행 중인 작업을 취소하며, 재활성화 뒤 도착한 이전 콜백도 폐기한 후 새 디코딩을 시작합니다. native와 Udon 모두 헤더·LUT 준비·payload에 같은 스냅샷을 사용합니다. 사용자 정의 코덱은 전달받은 이미지를 읽기 전용으로 다루고 영구적인 프레임 복사본처럼 보관하지 않아야 합니다. 프레임 역순이나 변수·RPC의 트랜잭션 적용을 해결하는 기능은 아닙니다.

## `ResetDecodeDiagnostics()`

```csharp
public void ResetDecodeDiagnostics()
```

Counters와 last-error fields를 초기화합니다. 반복 가능한 test나 scene wiring 변경 후 사용하세요.

## Diagnostics

| Member | 의미 |
| --- | --- |
| `lastFrameValid` | Last frame이 모든 decode stage를 통과했는지. |
| `lastHeaderValid` | Payload decode 전에 header가 valid였는지. |
| `lastError` | 마지막 decoder error 또는 warning text. |
| `lastNetworkMessageCount` | Decoded network frame의 message 수. |
| `lastAppliedVariableCount` | Behaviour에 적용된 variable 수. |
| `lastRpcCallCount` | Dispatch된 RPC message 수. |
| `lastCodecId` | Last valid header에서 읽은 codec ID. |
| `lastFrameIndex` | Header frame index. |
| `lastPayloadBytes` | Decoder가 사용한 payload byte count. |

## Binding application

Decoder는 `TSMPSetup`이 만든 binding arrays를 사용합니다. 각 binding은 다음을 연결합니다.

- `networkId`
- `variableHash`
- value type
- target behaviour
- target field name
- sync direction

Variable message가 도착하면 matching binding에만 적용합니다. RPC message는 matching `TSMPNetworkBehaviour`로 dispatch합니다.

## Runtime rules

- Header가 valid라도 알 수 없는 codec ID면 frame을 무시합니다.
- Target behaviour의 `ReceiveInterpolation.None`은 received values를 무시합니다.
- RPC는 event 성격입니다. Transport가 frame을 drop한다면 encoder repeat frames를 늘리세요.
- Uploaded world에서는 inspector field를 볼 수 없기 때문에 중요한 runtime failure는 log로 표시됩니다.
