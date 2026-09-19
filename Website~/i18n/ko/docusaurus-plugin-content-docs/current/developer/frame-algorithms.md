---
title: Frame과 Payload 알고리즘
---

# Frame and payload algorithms

여기서는 runtime data flow를 높은 수준에서 설명합니다. Encoder/decoder 변경을 검토하거나, test를 추가하거나, 왜 frame message가 예상보다 적은지 디버깅할 때 유용합니다.

## Encoder frame flow

```text
EncodeNow
  -> active TSMPNetworkBehaviour targets 수집
  -> network payload 시작
  -> 각 behaviour:
       TSMPBeforeEncode 호출
       enabled TransSync fields 기록
       비어 있는 VariableState message 취소
  -> queued RPC messages 기록
  -> message count patch
  -> CRC가 포함된 frame header 기록
  -> codec에 header/payload를 output texture로 그리도록 요청
```

중요한 동작:

- 빈 `VariableState` message는 rollback됩니다.
- RPC-only frame은 유효합니다.
- Queued RPC는 여러 frame에 반복될 수 있습니다.
- 현재 wire format에는 payload copy와 FEC가 없습니다.
- 선택한 codec이 payload start row, capacity, symbol mode, option bytes를 제공합니다.

## Network payload layout

```text
+-----------------------+-----------------------------+
| Network frame header  | Message 0 | Message 1 | ... |
| 8 bytes               | variable state or RPC       |
+-----------------------+-----------------------------+
```

Network frame header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 1 | Network major version. |
| `1` | 1 | Network minor version. |
| `2` | 2 | Message count. |
| `4` | 4 | Sequence. |

Message header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 2 | Network ID. |
| `2` | 1 | Message type. |
| `3` | 1 | Flags. |
| `4` | 2 | Message sequence. |
| `6` | 2 | Body length. |

## Variable state message

```text
+----------------+------------------+------------------+
| Variable count | Variable value 0 | Variable value N |
| 2 bytes        | 7-byte header + data                 |
+----------------+------------------+------------------+
```

Variable value header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 4 | Variable hash. |
| `4` | 1 | Value type. |
| `5` | 2 | Value byte length. |

Decoder는 variable hash와 network ID를 generated binding entry와 매칭합니다.

Encoder rule: behaviour에 대해 variable이 하나도 쓰이지 않으면 message를 취소하고 message count를 증가시키지 않습니다.

## RPC message

```text
+----------+----------------+----------------+----------------+
| RPC hash | Argument count | Argument 0     | Argument N     |
| 4 bytes  | 1 byte         | 3-byte header + data             |
+----------+----------------+----------------+----------------+
```

RPC argument header:

| Offset | Size | Field |
| --- | ---: | --- |
| `0` | 1 | Value type. |
| `1` | 2 | Value byte length. |

Decoder는 payload data에서 method name을 확인하고 `TSMPBehaviour.SendCustomEvent`를 통해 event를 dispatch합니다.

RPC event는 texture frame loss를 견디기 위해 encoder가 작은 frame 수만큼 반복합니다.

## Header validation

Decoder validation order:

1. Buffer range.
2. Magic.
3. Header size.
4. Major version.
5. Header CRC32.

CRC는 header bytes `0..51`에 대해 계산됩니다. Stored CRC는 bytes `52..55`에 있습니다. Mismatch가 있으면 payload processing 전에 frame을 버립니다.

## Decode flow

아래는 최초·폴백 경로입니다. 기본 예측 경로는 두 바이트 패스를 먼저 실행한 뒤 한 번 readback하고, 현재 헤더를 검증한 뒤에만 payload를 채택합니다. 메타데이터나 정확한 payload 길이가 달라지면 같은 스냅샷으로 기존 payload 디코딩을 수행하며, CRC 실패 시 프레임을 폐기합니다. 사용자 정의 코덱과 송신 포맷은 바뀌지 않습니다. 비교 필드·버퍼 배치·소유권은 [예측 readback](../scripting-api/decoder.md#predicted-readback)을 참고하세요.

```text
Update / decode tick
  -> 입력을 디코더 소유 스냅샷에 캡처
  -> 스냅샷에서 header 영역 읽기
  -> header와 CRC 검증
  -> codecId로 codec 선택
  -> 같은 스냅샷에서 PayloadSize만큼 payload bytes 요청
  -> network frame header decode
  -> messages 반복
  -> variable values 적용 또는 RPC dispatch
  -> diagnostics 갱신
```

잘못된 payload를 발견하면 이후 처리를 중단하고 진단을 기록합니다. 하지만 앞에서 이미 적용한 변수나 RPC의 효과를 되돌리지는 않습니다. 프레임 전체의 원자적 적용은 별도 과제입니다.

### 각 바이트 패스의 GPU 준비

헤더 패스(Luma4)와 선택한 payload 코덱은 모두 다음 순서로 실행됩니다.

```text
ApplyDecodeOptions
  -> 이번 패스의 바이트 머티리얼 설정
  -> PrepareDecode(source, byteMaterial)
       -> 이전 LUT 키워드 해제
       -> 선택적으로 기준 심볼을 float LUT에 기록
       -> LUT 연결 및 바이트 셰이더 variant 활성화
  -> 준비 패스와 같은 스냅샷에서 바이트 Blit
  -> 개별 readback 또는 헤더/payload를 묶어 예측 readback 1회
```

LUT가 없으면 바이트 셰이더는 payload 심볼 판정 중 기준 블록을 반복해서 읽습니다. LUT가 있으면 활성 패스마다 기준값을 한 번 계산하고, payload 샘플링과 판정은 그대로 수행합니다. 추가 패스 비용이 제거한 반복 작업보다 작을 때만 이득입니다. 작은 payload와 큰 payload 모두 준비와 디코드를 합쳐 측정하세요.

Luma4는 유효 샘플 크기가 1보다 클 때 16개 항목을 준비하고, 단일 샘플에서는 기존 경로를 사용합니다. 생성 텍스처는 linear ARGBFloat(채널당 32-bit float), Point 필터, mipmap 없는 설정입니다. Half·8-bit 양자화는 경계에서 판정 결과를 바꿀 수 있으므로 사용하지 않습니다.

할당만 재사용하고 프레임 사이의 값은 재사용하지 않습니다. 활성 바이트 패스마다 다시 그립니다. 리소스가 없으면 기존 셰이더 경로를 유지합니다. 코덱은 자신이 생성한 LUT를 소유하고 비활성화·파괴 시 정리하며, 머티리얼 소유권은 별도로 관리합니다.

`TSMPDecoder`는 헤더 패스 전에 입력을 각 슬롯이 소유한 같은 크기의 재사용 가능한 linear `ARGBFloat` RenderTexture로 캡처합니다. 헤더·캘리브레이션·payload 패스는 작업이 끝날 때까지 이 스냅샷을 읽습니다. 원본 영상이 갱신되거나 교체되어도 서로 다른 시점의 이미지가 섞이지 않습니다. 유휴 슬롯은 캡처 전에 크기를 변경할 수 있습니다. 비활성화하면 사용 중인 슬롯을 취소하고 유휴 리소스를 해제합니다. 대기 요청은 적용하지 않고 완료를 기다린 뒤 리소스를 해제하거나 재사용합니다. 제거 시 소유 텍스처를 해제합니다. 캡처 실패 시 갱신 중인 원본으로 되돌아가지 않고 작업을 거부합니다.

수락한 디코딩 시도마다 전체 이미지 GPU Blit 1회와 **할당된 슬롯별** 픽셀당 16바이트의 스냅샷 메모리가 추가됩니다. CPU readback이나 통신 포맷 필드는 추가하지 않습니다. 기본으로 켜진 [제한된 중첩](../scripting-api/decoder.md#bounded-readback-overlap)은 독립된 요청 상태로 최대 두 프레임을 보관하고 캡처 순서대로 적용합니다. 뒤 프레임이 먼저 완료되어도 앞 프레임의 폴백을 기다립니다. 용량이 차면 새 캡처를 건너뛰므로 대기열은 무한히 늘어나지 않습니다. Float32는 샘플링한 원본 값에 8비트·half-float 양자화 단계를 더하지 않기 위한 선택입니다. 소유권과 리소스 조건은 [decoder API](../scripting-api/decoder.md#input-snapshot)를 참고하세요. 이미 손상된 입력을 복구하거나 변수·RPC 적용을 트랜잭션으로 만드는 기능은 아닙니다.

훅, 머티리얼 설정, 라이프사이클, 대체 경로는 [구현 가이드](./codec-implementation.md), [셰이더 가이드](./codec-shaders.md), [준비 API](../scripting-api/codec.md#runtime-decode-preparation)를 참고하세요.

## Test를 추가할 위치

Protocol test 대상:

- Header CRC generation과 rejection.
- Message count patching.
- Empty variable message rollback.
- RPC-only frames.
- Payload capacity boundaries.
- Duplicate frame과 duplicate RPC handling.
