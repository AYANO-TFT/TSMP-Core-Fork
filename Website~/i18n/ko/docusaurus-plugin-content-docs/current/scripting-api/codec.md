---
title: TSMPCodec
---

# TSMPCodec

Namespace: `K13A.TSMP`

Codec handler의 base type입니다.

Codec handler는 encoder가 payload bytes를 pixels로 쓰는 방법과, decoder가 어떤 material/options로 payload bytes를 복원할지 알려줍니다.

## Public fields

| Field | Type | 용도 |
| --- | --- | --- |
| `codecId` | `ushort` | Frame header에 쓰는 stable codec ID. |
| `displayName` | `string` | Editor-facing name. |
| `codecOptionBytes` | `byte[]` | Frame header에서 복사된 decoder-side option bytes. |
| `selectedDecodeMaterial` | `Material` | Byte decode에 사용할 material. |
| `payloadStartRow` | `int` | Decode할 첫 payload row. |
| `payloadBlockCount` | `int` | Decode할 payload block 수. |
| `byteCount` | `int` | 요청된 payload byte count. |

Runtime encoder query result fields는 Udon event bridge를 위해 public입니다. `OnTSMPEncoderQuery()`와 `OnTSMPEncoderWritePayload()`가 할당합니다.

## Runtime encode methods

| Method | 용도 |
| --- | --- |
| `GetEncoderSymbolMode()` | Frame header용 symbol mode 반환. |
| `GetEncoderPayloadStartRow(width, blockSize)` | 첫 payload row 반환. |
| `GetEncoderPayloadCapacityBytes(width, height, blockSize)` | Payload byte capacity 반환. |
| `GetEncoderCodecOptionByteCount()` | Option byte count 반환, 최대 5. |
| `GetEncoderCodecOptionByte(index)` | Option byte 하나 반환. |
| `WriteEncoderPayload(...)` | Payload bytes를 encoder pixels에 씁니다. |
| `ApplyDecodeOptions()` | Header/options에서 decoder state를 적용합니다. |

VRChat 안에서 codec이 동작해야 한다면 이 methods는 UdonSharp-compatible해야 합니다.

## Helper methods

| Method | 용도 |
| --- | --- |
| `ReadCodecOptionByte(index, fallback)` | Fallback과 함께 option byte를 읽습니다. |
| `ReadCodecOptionFlag(index, fallback)` | Option을 boolean으로 읽습니다. |
| `GetEncoderActiveWidthBlocks(width, blockSize)` | Writable block width 계산. |
| `GetEncoderActiveHeightBlocks(height, blockSize)` | Writable block height 계산. |
| `WriteEncoderColorBlockAtIndex(...)` | Encoded block 하나를 color로 채웁니다. |
| `ReadEncoderBits(...)` | Payload bytes에서 arbitrary bits를 읽습니다. |

## Editor/native methods

`COMPILER_UDONSHARP` 밖에서 사용 가능:

| Method | 용도 |
| --- | --- |
| `SymbolMode` | Native symbol mode property. |
| `TryWriteFrame(...)` | Complete frame을 `Texture2D`에 씁니다. |
| `GetCodecOptionBytes()` | Native codec option byte array. |
| `DecodeMaterialCount` | Decode material 수. |
| `GetDecodeMaterial(index)` | Decode material lookup. |
| `DebugMaterialCount` | Debug material 수. |
| `GetDebugMaterial(index)` | Debug material lookup. |
| `ConfigureMaterials(context)` | Decode/debug materials 설정. |

## Udon event bridge

`TSMPCodec`은 encoder가 사용하는 public event method를 노출합니다.

| Method | 목적 |
| --- | --- |
| `OnTSMPEncoderQuery()` | Encoder query result fields를 채웁니다. |
| `OnTSMPEncoderWritePayload()` | `WriteEncoderPayload`를 호출하고 결과를 저장합니다. |

Encoder는 이 bridge를 사용해 optional codec package를 hard-code하지 않고 호출합니다.

Udon Encoder는 인코딩 시도마다 한 번 조회합니다. 같은 코덱 인스턴스의 옵션을 바꿔도 용량, payload 시작 행, header 옵션이 함께 갱신됩니다. Getter는 가볍고 부작용 없이 구현하세요. 조회 결과는 해당 인코딩 안에서만 재사용하며 다음 인코딩까지 고정하지 않습니다.

`OnTSMPEncoderQuery()`는 재사용되는 `int[10]`인 `encoderQueryValues`도 채웁니다. 순서는 codec ID, symbol mode, payload 시작 행, 바이트 용량, 옵션 개수, 옵션 바이트 다섯 개입니다. Bridge는 이 배열을 한 번에 읽습니다. 다음 조회에서 변경되는 읽기 전용 결과로 취급하고 설정 저장소로 사용하지 마세요. 기존 개별 결과 필드도 유지됩니다. 사용자 정의 코덱은 기존 Getter를 그대로 구현하면 되며 별도의 캐시 무효화 API는 필요하지 않습니다.

## 런타임 디코드 준비

`PrepareDecode(Texture source, Material material)`은 decoder가 입력 크기, 샘플 크기, 바이트 수와 레이아웃을 설정한 뒤, 각 header/payload 바이트 패스 직전에 호출됩니다. 바이트 패스가 읽을 것과 동일한 입력 스냅샷을 받습니다. GPU 준비 작업이 필요하면 재정의하고, 먼저 `base.PrepareDecode(source, material)`을 호출해 이전 LUT 키워드를 해제하세요. 기존 코덱은 재정의하지 않아도 됩니다.

protected 메서드 `GetDecodeSampleSize(material)`은 자동 샘플 크기를 결정하고 블록 크기 이내로 제한합니다. 디코드 셰이더와 같은 규칙입니다. `PrepareCalibrationLut(source, material, width)`는 `calibrationMaterial`로 재사용 가능한 1행 linear `ARGBFloat` 텍스처를 생성합니다. 바이트 머티리얼의 프로퍼티를 준비 머티리얼에 복사하고, 결과를 `_CalibrationLut`에 연결한 뒤 local `TSMP_CALIBRATION_LUT` 키워드를 켭니다. Player 빌드에도 두 경로가 포함되도록 셰이더에 `#pragma multi_compile_local _ TSMP_CALIBRATION_LUT`을 선언하세요.

준비 머티리얼은 코덱 프리팹에 지정합니다. 준비 셰이더는 LUT의 모든 픽셀을 기록해야 하며, 기록 중인 LUT를 읽으면 안 됩니다. 리소스 누락, Float32 생성 불가, 빈 출력, 추가 패스가 더 느린 모드에는 기존 디코드 경로를 유지하세요. 보정값을 Half나 8-bit로 양자화하면 디코딩된 바이트가 달라질 수 있습니다. LUT는 프레임 사이에 재사용하지 않고 매 패스마다 갱신합니다.

부모 클래스는 비활성화와 파괴 시 LUT를 해제합니다. 해당 라이프사이클 메서드를 재정의한다면 부모 구현도 호출하세요. 준비 훅을 직접 호출할 때는 decoder와 같은 머티리얼 프로퍼티를 설정한 후, 이어서 즉시 바이트 blit을 수행해야 합니다.
