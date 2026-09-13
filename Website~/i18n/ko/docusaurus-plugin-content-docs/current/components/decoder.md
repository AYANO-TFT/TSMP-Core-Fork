---
title: TSMPDecoder
---

# TSMPDecoder

`TSMPDecoder`는 수신자 쪽에서 사용합니다. TSMP 입력 텍스처를 읽고 디코딩된 메시지를 일치하는 씬 오브젝트에 적용합니다.

대부분의 decode 문제는 입력 텍스처가 변형 없는 TSMP 이미지를 담고 있지 않아서 발생합니다. Receiver component를 바꾸기 전에 texture path를 먼저 확인하세요.

## 필요한 것

- 인코딩된 TSMP frame을 담은 input texture.
- 호환되는 Luma4 handler.
- Payload byte texture.
- 일치하는 TSMP network behaviours와 bindings.
- Reference 지정 후 적용된 `TSMPSetup`.

## 사용 방법

1. Input texture를 지정합니다.
2. Sender와 같은 Luma4 경로를 사용합니다.
3. `TSMPSetup`을 통해 payload byte texture를 지정하거나 auto-size합니다.
4. Receiver object에 일치하는 TSMP network component가 있는지 확인합니다.
5. `Apply Setup`을 클릭합니다.
6. 로그 또는 `TSMPDebugCanvas`로 frame status를 확인합니다.

## 프레임 윈도우

실시간 수신에서는 디코더의 **Decode** 섹션에서 **Filter Frames**를 켜 두세요. **Window Size**는 기본 **256**프레임이며 수신기에서 조절합니다. 송신기에 같은 값을 설정할 필요는 없습니다.

중복되거나 오래된 번호는 무시합니다. 다만 마지막 적용 번호가 한 윈도우 이상일 때 0번이 도착하면 재시작으로 허용합니다. 기본값에서는 `256 -> 0`은 허용하고 `255 -> 0`은 거부합니다. UInt32 번호의 정상 순환은 별도로 처리합니다. 윈도우만큼 텍스처를 저장하지 않으며 데이터그램도 바뀌지 않습니다.

세션을 식별하는 기능은 아닙니다. 오래된 0번을 재시작으로 오인하거나 0번 유실로 재시작을 놓칠 수 있습니다. 녹화 영상의 원하는 시점으로 이동할 때는 필터를 끌 수 있지만 과거 변수값도 적용됩니다. [정확한 판정 규칙과 한계](../scripting-api/decoder.md#frame-window)를 참고하세요.

## 정상 decode 상태

`TSMPDebugCanvas`에서 정상 decoder는 다음을 보여야 합니다.

- `valid=yes`
- `header=yes`
- 시간이 지나며 바뀌는 frame index.
- 데이터가 전송 중일 때 0보다 큰 payload size.
- 전송 중인 데이터 종류와 맞는 message count.
- 안정적인 재생 중 낮은 loss.

Frame은 valid인데 object가 움직이지 않으면 network ID, receive interpolation, receiver object active state를 확인하세요.

## Decode 순서

Decoder는 단계적으로 동작합니다.

1. Input texture에서 header 영역을 읽습니다.
2. Magic, version, header size, CRC를 검증합니다.
3. Codec ID로 codec handler를 선택합니다.
4. Payload byte texture로 payload bytes를 decode합니다.
5. Payload bytes를 읽습니다.
6. Variable state messages와 RPC messages를 dispatch합니다.

어떤 단계가 실패하면 뒤 단계는 건너뜁니다. 예를 들어 CRC failure가 있으면 payload decoding은 시작되지 않습니다.

## Receive interpolation

각 `TSMPNetworkBehaviour`에는 receive mode가 있습니다.

| Mode | 결과 |
| --- | --- |
| None | 수신 값을 무시합니다. |
| Discrete | 받은 값을 바로 적용합니다. |
| Continuous | 지원하는 컴포넌트에서 target을 향해 부드럽게 보간합니다. |

컴포넌트가 데이터를 보내되 자신에게 들어오는 값은 적용하지 않아야 한다면 `None`을 사용합니다.

## CRC failures

디코더가 CRC mismatch를 기록하면 decode 전에 frame이 손상된 것입니다. 먼저 scaling, filtering, compression, Luma4 setting mismatch를 확인하세요.

일반적인 원인:

- OBS가 TSMP 이미지를 resize했습니다.
- Material 또는 camera effect가 pixel을 바꿨습니다.
- Receiver input이 잘못된 texture입니다.
- Header 영역이 crop되었습니다.
- Sender와 receiver의 texture layout settings가 맞지 않습니다.

## 흔한 해결 방법

| 증상 | 시도할 것 |
| --- | --- |
| `header=no` | 전체 TSMP 이미지가 decoder input에 도달하는지 확인합니다. |
| CRC warning과 `valid=no` | 전송 경로의 scaling/filtering/color processing을 제거합니다. |
| `msg=0` | Encoder payload가 0보다 크고 setup binding이 있는지 확인합니다. |
| RPC count가 0 | `TSMPNetworkGameObjectToggle`로 테스트하고 frame loss를 확인합니다. |
| 값은 수신되지만 적용되지 않음 | Receive interpolation과 active state를 확인합니다. |
