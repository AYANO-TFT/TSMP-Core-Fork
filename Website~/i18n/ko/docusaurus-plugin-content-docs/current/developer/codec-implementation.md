---
title: 코덱 구현 가이드
---

# Codec implementation guide

이 가이드는 C# handler, encoder logic, decode shader, materials, package metadata, validation까지 codec 구현 전체를 다룹니다.

먼저 [Custom codec](./custom-codec.md)을 읽는 것을 권장합니다. Custom codec은 package-level contract를 설명하고, 여기서는 실제 구현에서 자주 버그가 나는 부분을 설명합니다.

## 구현 순서

Codec은 다음 순서로 만드세요.

1. Stable `codecId`와 symbol mode를 정합니다.
2. Capacity calculation을 구현합니다.
3. C# payload writing을 구현합니다.
4. Written pixels에서 bytes를 복원하는 decode shader를 작성합니다.
5. Decode materials를 설정합니다.
6. Codec prefab/catalog를 등록합니다.
7. 실제 scene data를 쓰기 전에 byte-for-byte round trip을 검증합니다.

처음부터 복잡한 shader를 만들지 마세요. 먼저 작은 payload가 한 frame 안에서 round-trip되는지 확인하세요.

## Codec pipeline

```text
TSMPEncoder
  -> header bytes와 payload bytes 생성
  -> TSMPCodec에 symbol mode, start row, capacity, options 질의
  -> payload bytes와 pixel buffer를 TSMPCodec에 전달
  -> TSMPCodec이 encoded pixels 작성
  -> output texture가 capture/transport됨

TSMPDecoder
  -> frame header 읽기
  -> CRC 검증
  -> codecId로 codec 선택
  -> codec options 적용
  -> codec decode material을 byte texture로 실행
  -> payload bytes를 읽고 messages dispatch
```

Encoder는 codec-specific class를 알면 안 됩니다. 선택된 `TSMPCodec`을 base API로 호출합니다. 이렇게 해야 optional codec package가 Core와 독립적으로 유지됩니다.

## Package layout

Runtime code, shaders, materials, prefabs, samples를 함께 두는 package layout을 사용하세요.

```text
com.example.tsmp.codec.mycodec/
  package.json
  Runtime/
    Scripts/
      TSMPCodecMyCodec.cs
    Shaders/
      TSMPDecodeMyCodecBytes.shader
      TSMPDebugMyCodecCalibration.shader
    Materials/
      GPUDecoderMyCodec.mat
    Prefabs/
      TSMPCodecMyCodec.prefab
  Samples/
```

Package는 Core에 의존해야 합니다.

```json
"dependencies": {
  "com.kibalab.tsmp.core": "0.3.0-beta.2"
}
```

이 예제는 현재 배포 후보인 Core **0.3.0-beta.2**를 기준으로 하며, 이미 공개된 버전이라는 뜻은 아닙니다. Core 0.2.0과 0.3.0-beta.1에는 준비 API가 없습니다. 호환 Core가 공개되고 배포 패키지 조합을 검증한 뒤 의존 코덱을 공개하세요.

`vpmDependencies`에도 `"com.kibalab.tsmp.core": ">=0.3.0-beta.2"`를 지정합니다. UPM 패키지 의존성에는 범위나 Git URL이 아닌 버전 문자열을 사용합니다. 로컬/디스크 또는 Git 설치에서는 **프로젝트**에 호환 Core도 직접 지정해야 합니다. 이 manifest만으로 GitHub 위치를 찾지는 못합니다. VPM은 등록된 저장소에서 범위를 해결하며 베타 선택 시 시험판 표시를 켜야 합니다.

준비 머티리얼 누락 시 대체 경로는 컴파일 이후에만 동작합니다. 이전 부모 클래스와 새 코덱의 컴파일 호환성을 제공하지는 않습니다. 새 API를 사용하지 않는 기존 코덱은 이전에 검증한 최소 버전을 유지할 수 있습니다.

[Unity package manifest](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-manifestPkg.html), [VPM 버전과 범위](https://vcc.docs.vrchat.com/vpm/packages/#versions-and-ranges)를 참고하세요.

## 1. Symbol model 선택

Encoded block 하나가 몇 bit를 운반하는지 정의합니다.

예시:

| Model | 의미 |
| --- | --- |
| 4-bit luminance | Payload byte 하나당 두 block. |
| 8-bit color index | Payload byte 하나당 한 block. |
| N-bit RGB symbol | `ceil(payloadBits / N)` blocks. |

정할 것:

- Stable symbol mode integer.
- Stable `codecId`.
- Calibration block 필요 여부.
- Codec option 필요 여부.

Frame header에는 최대 5개의 codec option byte가 있습니다.

Release 후에는 `codecId`와 symbol mode를 안정적으로 유지하세요. 둘 중 하나를 바꾸면 해당 codec의 wire format 변경입니다.

## 2. C# handler 구현

`TSMPCodec` subclass를 만듭니다.

```csharp
using K13A.TSMP;
using UnityEngine;

[AddComponentMenu("TSMP/Codecs/My Codec")]
public sealed class TSMPCodecMyCodec : TSMPCodec
{
    private const int SymbolModeMyCodec = 128;
    private const int BitsPerSymbol = 8;

    public Material byteDecodeMaterial;

    public override int GetEncoderSymbolMode()
    {
        return SymbolModeMyCodec;
    }

    public override int GetEncoderPayloadStartRow(int width, int blockSize)
    {
        return 5;
    }

    public override int GetEncoderPayloadCapacityBytes(int width, int height, int blockSize)
    {
        int activeWidthBlocks = GetEncoderActiveWidthBlocks(width, blockSize);
        int activeHeightBlocks = GetEncoderActiveHeightBlocks(height, blockSize);
        int payloadRows = Mathf.Max(0, activeHeightBlocks - GetEncoderPayloadStartRow(width, blockSize) - 1);
        return activeWidthBlocks * payloadRows * BitsPerSymbol / 8;
    }

    public override bool WriteEncoderPayload(
        Color32[] pixels,
        int width,
        int height,
        int blockSize,
        byte[] payloadBytes,
        int payloadByteCount)
    {
        int activeWidthBlocks = GetEncoderActiveWidthBlocks(width, blockSize);
        int payloadStartBlock = GetEncoderPayloadStartRow(width, blockSize) * activeWidthBlocks;

        for (int i = 0; i < payloadByteCount; i++)
            WriteEncoderColorBlockAtIndex(pixels, width, height, blockSize, payloadStartBlock + i, ByteToColor(payloadBytes[i]));

        return true;
    }

    public override void ApplyDecodeOptions()
    {
        selectedDecodeMaterial = byteDecodeMaterial;
        payloadStartRow = GetEncoderPayloadStartRow(0, 8);
        payloadBlockCount = byteCount;
    }

    private static Color32 ByteToColor(byte value)
    {
        return new Color32(value, value, value, 255);
    }

#if !COMPILER_UDONSHARP
    public override int SymbolMode => SymbolModeMyCodec;
    public override int DecodeMaterialCount => byteDecodeMaterial != null ? 1 : 0;
    public override Material GetDecodeMaterial(int index) => index == 0 ? byteDecodeMaterial : null;
#endif
}
```

Runtime path는 UdonSharp-compatible하게 유지하세요. Generic methods, LINQ, reflection, unsupported Unity APIs를 피합니다.

## 3. Encoder logic 작성

Encoder-facing method는 `WriteEncoderPayload`입니다.

| Parameter | 의미 |
| --- | --- |
| `pixels` | Writable output pixel 또는 block buffer. |
| `width`, `height` | Output frame dimensions. |
| `blockSize` | Encoded block 하나의 크기. |
| `payloadBytes` | Payload buffer. |
| `payloadByteCount` | 유효한 payload byte 수. |

규칙:

- Active frame area 밖에 쓰지 않습니다.
- Header/base regions를 손상시키지 않습니다.
- Codec이 소유한 payload area에만 씁니다.
- 필요한 state가 없으면 `false`를 반환합니다.
- Unused capacity를 일관되게 clear하거나 ignore합니다.

자주 쓰는 `TSMPCodec` helper:

- `GetEncoderActiveWidthBlocks`
- `GetEncoderActiveHeightBlocks`
- `WriteEncoderColorBlockAtIndex`
- `ReadEncoderBits`

### Capacity는 shader와 반드시 일치해야 함

가장 흔한 codec bug는 다음 세 값의 불일치입니다.

- Encoder payload capacity.
- Encoder payload start block.
- Decoder shader `_StartBlock`과 `_ByteCount`.

이 값들이 다르면 header는 정상 decode되지만 payload message가 손상되거나 사라질 수 있습니다.

## 4. Codec options 추가

Bit depth, refinement mode, calibration mode 같은 decode 선택에는 codec option bytes를 사용합니다.

```csharp
public override int GetEncoderCodecOptionByteCount()
{
    return 2;
}

public override int GetEncoderCodecOptionByte(int index)
{
    if (index == 0)
        return mode;
    if (index == 1)
        return refine ? 1 : 0;

    return 0;
}
```

Decode 쪽에서는 `ApplyDecodeOptions()`에서 읽습니다.

```csharp
public override void ApplyDecodeOptions()
{
    int mode = ReadCodecOptionByte(0, 0);
    bool refine = ReadCodecOptionFlag(1, false);
}
```

## 5. Decode shader 작성

Decode shader는 encoded pixels를 byte pixels로 변환합니다. 출력 byte texture에서는 RGBA pixel 하나가 decoded bytes 네 개를 나타냅니다.

Shader contract:

```text
DecodeByte(byteIndex) -> integer 0..255
```

`TSMPDecodeByteOutput.cginc`는 decoded bytes 네 개를 output pixel 하나로 pack합니다. Codec shader는 해당 symbol representation에서 byte를 복원하는 `DecodeByte`만 구현하면 됩니다.

기본 구조:

```hlsl
Shader "Hidden/TSMP/Decode MyCodec Bytes"
{
    Properties
    {
        _MainTex ("TSMP Source", 2D) = "black" {}
        _BlockSize ("Block Size", Float) = 8
        _SampleSize ("Sample Size", Float) = 0
        _StartBlock ("Start Block", Float) = 0
        _ByteCount ("Byte Count", Float) = 0
        _ActiveWidthBlocks ("Active Width Blocks", Float) = 80
        _SourceWidth ("Source Width", Float) = 640
        _SourceHeight ("Source Height", Float) = 360
        _OutputWidth ("Output Width", Float) = 14
        _OutputHeight ("Output Height", Float) = 1
        _FlipY ("Flip Y", Float) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeCommon.cginc"

            int DecodeByte(int byteIndex)
            {
                if (byteIndex < 0 || byteIndex >= (int)_ByteCount)
                    return 0;

                float blockIndex = _StartBlock + byteIndex;
                float3 rgb = SampleBlockByIndex(blockIndex);
                return (int)round(saturate(rgb.r) * 255.0);
            }

            #include "Packages/com.kibalab.tsmp.core/Runtime/Codecs/Common/Shaders/cgincs/TSMPDecodeByteOutput.cginc"
            ENDCG
        }
    }

    Fallback Off
}
```

## Include paths

공유 TSMP shader file은 package include path로 참조하세요. 이렇게 하면 codec shader가 project `Assets` layout에 의존하지 않습니다.

## Shader properties

Decode material은 `TSMPCodec.ConfigureDecodeMaterial`이 사용하는 표준 property를 지원하는 것이 좋습니다.

| Property | 의미 |
| --- | --- |
| `_MainTex` | Source TSMP texture. |
| `_BlockSize` | Encoded block size in pixels. |
| `_SampleSize` | Header/setup의 decode sample size. |
| `_StartBlock` | Decode할 첫 payload block. |
| `_ByteCount` | 요청된 payload byte 수. |
| `_ActiveWidthBlocks` | Frame의 active block width. |
| `_SourceWidth` / `_SourceHeight` | Source frame dimensions. |
| `_OutputWidth` / `_OutputHeight` | Byte texture dimensions. |
| `_FlipY` | Source sampling vertical flip 여부. |

## 6. Calibration 추가

Transport가 색을 바꾼다면 payload block 앞에 calibration symbols를 둘 수 있습니다.

일반 패턴:

1. Encoder가 known symbol table을 씁니다.
2. Decode shader가 calibration table을 sampling합니다.
3. Decode shader가 payload block을 가장 가까운 calibration entry로 분류합니다.

필요하면 material property로 calibration start block을 노출하세요. Editor/native path는 `ConfigureMaterials(CodecMaterialContext context)`, runtime path는 `ApplyDecodeOptions()`에서 설정합니다.

## 선택적 캘리브레이션 준비

캘리브레이션은 수신 영상의 알려진 기준 심볼을 측정한 뒤 payload 샘플과 비교하는 과정입니다. 출력 바이트마다 같은 기준 블록을 다시 읽으면 작업이 반복됩니다. LUT(조회 테이블)를 사용하면 기준값 계산을 한 번의 GPU 준비 패스로 옮길 수 있습니다.

LUT는 계산 위치만 바꿉니다. 패킷 형식이나 심볼 판정 알고리즘은 바꾸지 않습니다. 추가 패스의 비용이 더 큰 코덱은 기존 디코드 경로를 유지하세요.

### 호출 순서

Decoder는 **각 헤더 또는 payload 바이트 패스마다** 다음 순서로 처리합니다.

1. 코덱을 선택하고 `ApplyDecodeOptions()`로 바이트 머티리얼과 레이아웃을 결정합니다.
2. 해당 머티리얼에 `_MainTex`, 입력 크기, 블록/샘플 크기, `_StartBlock`, `_ByteCount`, 바이트 출력 크기, `_FlipY`를 설정합니다.
3. `PrepareDecode(source, material)`을 호출합니다. 코덱은 이번 패스의 설정으로 캘리브레이션을 준비할 수 있습니다.
4. 곧바로 같은 원본 Texture 참조를 바이트 머티리얼로 Blit합니다.
5. LUT가 아닌 바이트 출력 텍스처에서 readback을 요청합니다.

`ApplyDecodeOptions()` 안에서 준비하지 마세요. 이 시점에는 해당 패스의 프로퍼티가 모두 설정되지 않았습니다. `Start()`, 코덱 변경 시점, 프레임당 한 번만 준비하는 것도 충분하지 않습니다. 헤더와 payload 패스는 설정과 실행 시점이 다를 수 있습니다.

`TSMPDecoder`는 헤더 패스 전에 입력을 자체 스냅샷으로 복사하고, 해당 디코딩 작업의 모든 준비·바이트 패스에 이 스냅샷을 제공합니다. 전달받은 텍스처를 수정하거나 해제하지 마세요. 영상 공급자가 원본을 갱신해도 헤더와 payload는 같은 캡처 이미지를 읽습니다. 준비 훅 자체가 영상을 복사하는 것은 아니므로, `TSMPDecoder` 없이 코덱을 호출하는 통합 코드는 입력을 직접 고정해야 합니다.

### Luma4 최소 훅

Luma4 핸들러는 아래 재정의를 사용합니다. 기존 코덱 클래스 안에서 인코드 메서드 및 `ApplyDecodeOptions()`와 함께 구현합니다.

```csharp
public override void PrepareDecode(Texture source, Material material)
{
    base.PrepareDecode(source, material);
    if (material == null || GetDecodeSampleSize(material) <= 1)
        return;

    PrepareCalibrationLut(source, material, 16);
}
```

Luma4의 기준 심볼은 16개이므로 테이블은 16×1입니다. 유효 샘플 크기가 1일 때는 측정 환경에서 준비 비용이 절감량보다 컸으므로 기존 경로를 유지합니다. 이는 Luma4의 정책이며 Core가 다른 코덱에 강제하는 규칙이 아닙니다. 사용자 정의 코덱은 준비 비용을 포함해 직접 측정한 뒤 조건을 선택하세요.

코덱 프리팹의 상속된 `calibrationMaterial` 필드에 Luma4 준비 셰이더를 사용하는 머티리얼을 지정합니다. 이 필드는 일반 Inspector에서 숨겨져 있습니다. 패키지의 에디터 도구나 Inspector Debug 모드에서 직렬화 필드를 설정하세요. 최종 사용자는 별도의 설정 작업 없이 완성된 catalog/프리팹을 받아야 합니다.

바이트 셰이더에도 `_CalibrationLut`, `TSMP_CALIBRATION_LUT` local variant와 기존 샘플링 경로가 필요합니다. 구체적인 구현은 [셰이더 가이드](./codec-shaders.md)를 참고하세요. C# 훅만 추가한다고 셰이더가 LUT를 사용하게 되지는 않습니다.

### 리소스 소유권과 대체 경로

- 코덱 부모 클래스가 생성한 LUT RenderTexture를 소유합니다. 할당은 재사용하되 활성화된 패스마다 내용을 다시 그리며, 비활성화·파괴 시 해제하고 제거합니다.
- `calibrationMaterial`과 바이트 머티리얼은 외부에서 지정한 참조입니다. 훅이 만들지는 않지만 프로퍼티는 변경합니다. Setup은 컨트롤러별 머티리얼 복사본을 준비합니다. 직접 연동할 때도 공유 머티리얼의 사용이 충돌하지 않게 관리하세요.
- 활성화 조건을 검사하기 전에 항상 `base.PrepareDecode(source, material)`을 호출하세요. 같은 머티리얼이 기존 경로로 돌아갈 때도 이전 LUT 키워드를 꺼야 합니다.
- `OnDisable()` 또는 `OnDestroy()`를 재정의하면 해당 부모 구현을 호출하세요. 패키지 머티리얼 에셋이나 외부에서 받은 입력/출력 텍스처를 파괴하지 마세요.
- 원본, 바이트 머티리얼, 준비 머티리얼이 없거나 LUT 너비가 잘못되었거나 출력 바이트가 없으면 준비를 생략합니다. Native 경로는 지원되지 않는 준비 셰이더와 ARGBFloat 장치도 검사합니다. 양쪽 경로 모두 실제 포맷과 텍스처 생성 결과를 검사하므로 기존 셰이더 variant를 유지해야 합니다.
- 1행 linear `ARGBFloat`(채널당 32-bit float), Point 필터, mipmap 없는 설정을 사용하세요. Half·8-bit·sRGB 저장으로 대체하면 반올림 때문에 판정 경계의 심볼 결과가 바뀔 수 있습니다.
- 준비 패스가 여러 개라면 실행 순서를 정하고 각 출력을 완전히 덮어쓰세요. 쓰고 있는 LUT를 동시에 읽지 마세요.

메서드 선언, 검사 조건, 라이프사이클은 [TSMPCodec Scripting API](../scripting-api/codec.md#runtime-decode-preparation)를 참고하세요. 리소스 누락, 샘플 크기 변경, 비활성화·재활성화, 여러 코덱 인스턴스, Player의 두 셰이더 variant를 바이트 단위로 비교 검증하세요.

## 7. Materials 설정

각 decode shader에 대한 material을 만들고 codec component에 지정합니다.

Native/editor material configuration:

```csharp
#if !COMPILER_UDONSHARP
public override int DecodeMaterialCount => byteDecodeMaterial != null ? 1 : 0;

public override Material GetDecodeMaterial(int index)
{
    return index == 0 ? byteDecodeMaterial : null;
}

public override void ConfigureMaterials(CodecMaterialContext context)
{
    base.ConfigureMaterials(context);
}
#endif
```

Runtime decode에서는 `ApplyDecodeOptions()`가 selected material과 runtime fields를 설정해야 합니다.

```csharp
public override void ApplyDecodeOptions()
{
    selectedDecodeMaterial = byteDecodeMaterial;
    payloadStartRow = 5;
    payloadBlockCount = byteCount;
}
```

## 8. Editor/native frame writing 구현

Editor tool 또는 native C# path에서 encode해야 한다면 `TryWriteFrame`을 구현합니다.

```csharp
#if !COMPILER_UDONSHARP
public override bool TryWriteFrame(Texture2D texture, int blockSize, byte[] headerBytes, byte[] payloadBytes, out string error)
{
    if (!ValidateRasterFrame(texture, blockSize, headerBytes, payloadBytes, out error))
        return false;

    Color32[] pixels = FrameRaster.CreateClearedPixels(texture.width, texture.height);
    Luma4Raster.WriteBaseRegions(pixels, texture.width, texture.height, blockSize, headerBytes);
    WriteEncoderPayload(pixels, texture.width, texture.height, blockSize, payloadBytes, payloadBytes.Length);
    FrameRaster.WriteEndMarker(pixels, texture.width, texture.height, blockSize);
    texture.SetPixels32(pixels);
    texture.Apply(false, false);
    return true;
}
#endif
```

이 path는 UdonSharp runtime encoding과 분리되어 있지만 같은 frame layout을 만들어야 합니다.

## 9. Codec 등록

`TSMPSetup`에 나타나게 하려면:

1. Codec component가 붙은 prefab을 만듭니다.
2. `codecId`, `displayName`, materials, options를 지정합니다.
3. Prefab을 참조하는 `TSMPCodecCatalog` asset을 추가합니다.
4. `package.json`에 package metadata를 넣습니다.
5. `TSMPSetup`에서 codecs를 refresh합니다.

Encoder가 custom codec type을 hard-code하면 안 됩니다. 항상 `TSMPCodec`을 통해 호출되어야 합니다.

## 10. Codec 검증

최소 검증:

- Header bytes가 올바르게 decode됩니다.
- Payload bytes가 정확히 round-trip됩니다.
- Payload capacity calculation이 실제 writable capacity와 맞습니다.
- Payload start row가 shader `_StartBlock`과 맞습니다.
- Codec option bytes가 decoder에 도착합니다.
- Header pixel을 손상시키면 CRC mismatch가 보고됩니다.
- Payload가 줄어들 때 output이 clear됩니다.
- Unity editor path와 UdonSharp runtime path가 호환 frame을 만듭니다.

권장 첫 payload:

```text
00 01 02 03 04 05 06 07 08 09 FE FF
```

이 값은 실제 TSMP payload를 테스트하기 전에 byte order, clamping, zero handling, high-value handling을 잡는 데 도움이 됩니다.

## 흔한 실패 원인

| 증상 | 가능성 높은 원인 |
| --- | --- |
| Header는 valid인데 payload가 invalid | Payload start row 또는 byte capacity mismatch. |
| CRC mismatch | Header region이 손상되었거나 shader가 잘못된 pixel을 읽습니다. |
| Editor에서는 되지만 Udon에서 실패 | Runtime path가 unsupported API를 사용하거나 native path와 다릅니다. |
| 이전 데이터가 남아 보임 | Output clearing 또는 unused-region handling이 불완전합니다. |
| Decoder가 너무 적은 bytes를 읽음 | `payloadBlockCount` 또는 `_ByteCount`가 잘못되었습니다. |
| Setup에 codec이 나타나지 않음 | Catalog, prefab reference, package metadata가 누락되었습니다. |
