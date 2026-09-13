---
title: Codec Shaders
---

# Codec shaders

TSMP codec shader는 payload bytes와 visible frame pixels 사이를 변환합니다. 기본 Luma4 codec을 reference path로 볼 수 있지만, custom codec은 자신만의 material과 shader pass를 사용할 수 있습니다.

## Encode side

Encode shader 또는 material은 보통 다음 input을 받습니다.

| Input | 용도 |
| --- | --- |
| Payload byte texture 또는 buffer texture | Encoder가 배치한 source bytes. |
| Header pixels | Core encoder가 준비한 header area. |
| Codec option bytes | 작은 codec-specific settings. |
| Block size | Symbol output용 pixel block dimensions. |
| Payload size | 그려야 하는 valid bytes 수. |

Encode result는 header를 읽을 수 있게 유지하고, codec이 보고한 layout에 payload symbols를 배치해야 합니다.

## Decode side

Decode shader 또는 material은 보통 다음 input을 받습니다.

| Input | 용도 |
| --- | --- |
| Source TSMP frame | Camera, OBS, Spout 등에서 받은 captured texture. |
| Header metadata | Payload size, block size, sample size, codec option bytes. |
| Payload layout | Start row/block and block count. |
| Byte output texture | Readback할 recovered bytes를 저장하는 texture. |

Decoder는 header의 payload byte count만 읽습니다. Unused pixels가 결과에 영향을 주면 안 됩니다.

Recovered bytes를 RGBA byte texture로 출력하는 decode shader는 [`TSMPDecodeByteOutput.cginc` scripting API](../scripting-api/decode-byte-output.md)를 참고하세요.

## 준비 셰이더와 바이트 셰이더

LUT를 사용하는 코덱에는 서로 다른 두 출력이 있습니다.

| 패스 | 출력 | 의미 |
| --- | --- | --- |
| 준비 | 1행 linear ARGBFloat LUT | 측정한 캘리브레이션 값이며 바이트가 아닙니다. |
| 바이트 디코드 | RGBA 바이트 출력 | CPU readback을 위한 픽셀당 복원 바이트 4개입니다. |

순서는 머티리얼 설정, `PrepareDecode(source, material)`, 필요한 경우 준비 Blit, 바이트 Blit입니다. 준비와 바이트 패스는 같은 입력, 샘플 크기, 블록 좌표, 상하 방향을 사용해야 합니다. [Luma4 C# 훅과 소유권 규칙](./codec-implementation.md), [TSMPCodec API](../scripting-api/codec.md#runtime-decode-preparation)를 함께 참고하세요.

### 1. 캘리브레이션 계산 공유

Luma4는 16개 심볼 각각을 1번 행의 기준 블록 2개에서 측정합니다. 코덱 전용 include에 아래 함수를 두고, 두 셰이더 모두 `TSMPDecodeCommon.cginc` 다음에 포함하세요.

```hlsl
#if defined(TSMP_CALIBRATION_LUT)
Texture2D<float4> _CalibrationLut;
#endif

float CalibrationLuma(int symbol)
{
#if defined(TSMP_CALIBRATION_LUT)
    return _CalibrationLut.Load(int3(symbol, 0, 0)).r;
#else
    float a = SampleBlockLuma(symbol * 2, 1.0);
    float b = SampleBlockLuma(symbol * 2 + 1, 1.0);
    return (a + b) * 0.5;
#endif
}
```

활성 경로는 정수 텍셀 `Load`를 사용해 항목 사이의 보간을 피합니다. 비활성 경로는 원래 샘플 위치, 평균 계산, `float` 정밀도를 유지합니다. Payload 심볼 판정은 그대로 두세요. 캘리브레이션 정밀도를 낮추는 것은 바이트 결과를 보존하는 최적화가 아닙니다.

### 2. 준비 텍셀 전체 쓰기

표준 입력/블록/샘플/flip 프로퍼티, `Cull Off`, `ZWrite Off`, `ZTest Always`, target 3.5, 공통 vertex 함수를 사용하는 준비 셰이더를 만듭니다. `TSMPDecodeCommon.cginc`와 위 캘리브레이션 함수를 포함하고 아래 fragment 함수를 사용합니다.

```hlsl
float4 frag(v2f i) : SV_Target
{
    int symbol = (int)floor(i.pos.x);
    return CalibrationLuma(symbol).xxxx;
}
```

이 예제의 출력은 16×1입니다. `_OutputWidth`와 `_OutputHeight`에는 **바이트** 머티리얼의 크기가 복사되므로 LUT 위치 계산에는 사용하지 마세요. 위 코드는 실제 래스터 위치로 항목을 결정합니다.

준비 셰이더에는 LUT 키워드 variant를 **컴파일하지 않고**, `TSMPDecodeByteOutput.cginc`도 **포함하지 않습니다**. 항상 원본 영상에서 측정해 float 값을 씁니다. 특히 출력 중인 `_CalibrationLut`를 읽으면 안 됩니다. 이 패스에서는 blending, sRGB 변환, mipmap, Half 변환, RGBA8 바이트 패킹을 사용하지 마세요.

### 3. 두 바이트 디코드 variant 유지

바이트 셰이더의 `Properties`에 `[HideInInspector] _CalibrationLut ("Calibration LUT", 2D) = "black" {}`를 선언하고 프로그램에 아래 구문을 추가합니다.

```hlsl
#pragma multi_compile_local _ TSMP_CALIBRATION_LUT
```

같은 캘리브레이션 함수를 포함하고 기존 `DecodeByte(int byteIndex)`를 유지한 뒤 `TSMPDecodeByteOutput.cginc`를 포함합니다. 부모 준비 훅이 패스 시작 시 키워드를 끄고, LUT를 준비한 경우에만 다시 켭니다. 저장된 머티리얼에서 키워드가 꺼져 있다는 이유로 빌드에서 제거되지 않도록 `multi_compile_local`을 사용하세요.

준비 머티리얼과 바이트 머티리얼은 별개여야 합니다. 헬퍼는 바이트 머티리얼의 프로퍼티를 준비 머티리얼에 복사하지만 셰이더를 교체하지는 않습니다. 코덱 프리팹에 두 머티리얼을 지정하고 패키지에 셰이더 참조도 유지하세요.

### 4. 두 패스를 함께 검증

마지막 바이트 셰이더만 비교하지 말고 준비와 바이트 패스를 합친 비용을 원래 경로와 비교하세요. 작은 payload에서는 추가 Blit 비용이 더 클 수 있습니다. Luma4는 유효 샘플 크기 1에서는 원래 경로를, 더 큰 샘플에서는 준비 경로를 사용합니다.

깨끗한 색과 변형된 색, 양쪽 Y 방향, 자동/명시적 샘플 크기, RGBA 일부만 사용하는 출력, 입력 변경, 준비 머티리얼 누락에서 바이트가 정확히 일치하는지 확인하세요. 패키지 프리팹, 비활성화·재활성화, 여러 컨트롤러도 검사하세요. Player를 실제 빌드·실행해 local variant 둘 다 포함되는지 확인해야 하며, Editor 시험만으로는 충분하지 않습니다.

LUT 할당은 재사용하지만 활성 패스마다 내용을 갱신합니다. Texture 참조를 저장한다고 영상의 픽셀이 고정되지는 않습니다. 프레임 사이의 캘리브레이션 재사용이나 헤더/payload 스냅샷 보장은 이 최적화에 포함되지 않습니다.

## Shader include paths

Package shader는 package-stable include path를 사용해야 합니다. Scene folder 위치에 의존하는 relative include path는 package 위치가 바뀔 때 깨지기 쉽습니다.

VPM package로 설치되어도 동작하는 include 방식을 선호하세요.

## Precision rules

- Header sampling은 정확해야 합니다.
- Payload symbol에는 filtering을 피하세요.
- Byte recovery는 point sampling을 사용하세요.
- 현재 payload가 사용하는 output pixel은 모두 clear 또는 overwrite하세요.
- 가능하면 unused output pixel도 deterministic하게 유지해 stale block을 피하세요.

## Material properties

Custom codec은 shader properties를 설명해야 합니다. 흔한 property는 다음과 같습니다.

| Property | Meaning |
| --- | --- |
| `_MainTex` | Source frame 또는 payload texture. |
| `_PayloadByteTexture` | Intermediate byte texture. |
| `_PayloadSize` | Valid payload byte count. |
| `_BlockSize` | Codec block size. |
| `_SampleSize` | Decode sample size. |
| `_CodecOptionBytes` | Codec-specific option vector. |

Editor setup과 runtime code가 special case 없이 assign할 수 있도록 stable property name을 사용하세요.

## Validation

Codec shader 배포 전 확인하세요.

- Deterministic byte sequence encode/decode.
- Non-full payload sizes.
- Codec switch 후 stale block 없음.
- Capture scaling disabled 상태.
- 실제 사용자가 쓸 render texture size.
