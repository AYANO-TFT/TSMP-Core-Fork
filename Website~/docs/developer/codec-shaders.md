---
title: Codec Shaders
---

# Codec shaders

A TSMP codec shader converts between payload bytes and visible frame pixels. The default Luma4 codec is available as a reference path, but custom codecs can use their own materials and shader passes.

## Encode side

An encode shader or material usually receives:

| Input | Purpose |
| --- | --- |
| Payload byte texture or buffer texture | Source bytes arranged by the encoder. |
| Header pixels | Header area already prepared by the core encoder. |
| Codec option bytes | Small codec-specific settings. |
| Block size | Pixel block dimensions for symbol output. |
| Payload size | Number of valid bytes to draw. |

The encode result must leave the header readable and place payload symbols at the layout reported by the codec.

## Decode side

A decode shader or material usually receives:

| Input | Purpose |
| --- | --- |
| Source TSMP frame | Captured texture from camera, OBS, Spout, or another path. |
| Header metadata | Payload size, block size, sample size, and codec option bytes. |
| Payload layout | Start row/block and block count. |
| Byte output texture | Texture that stores recovered bytes for readback. |

The decoder reads only the payload byte count from the header. Unused pixels should not affect the result.

For decode shaders that output recovered bytes to an RGBA byte texture, see the [`TSMPDecodeByteOutput.cginc` scripting API](../scripting-api/decode-byte-output.md).

## Preparation shader and byte shader

A LUT-enabled codec uses two different outputs:

| Pass | Output | Meaning |
| --- | --- | --- |
| Preparation | One-row linear ARGBFloat LUT | Measured calibration values, not bytes. |
| Byte decode | RGBA byte output | Four recovered bytes per pixel for CPU readback. |

The order is material configuration, `PrepareDecode(source, material)`, preparation Blit when enabled, then byte Blit. Preparation must use the same source, sample size, block coordinates and orientation as that byte pass. See the [Luma4 C# hook and ownership rules](./codec-implementation.md) and [TSMPCodec API](../scripting-api/codec.md#runtime-decode-preparation).

### 1. Share the calibration calculation

For Luma4, each of the 16 symbols is measured from two reference blocks on row 1. Put this function after `TSMPDecodeCommon.cginc` in a codec-local include, and include it from both shaders:

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

The enabled path uses an integer texel `Load`, avoiding interpolation between entries. The disabled path keeps the original sample positions, averaging and `float` precision. Keep the payload symbol classifier unchanged: replacing its input with lower-precision calibration is not a byte-preserving optimization.

### 2. Write every preparation texel

Create a preparation shader with the standard source/block/sample/flip properties, `Cull Off`, `ZWrite Off`, `ZTest Always`, target 3.5 and the common vertex function. Include `TSMPDecodeCommon.cginc`, then the calibration function above. Its fragment entry is:

```hlsl
float4 frag(v2f i) : SV_Target
{
    int symbol = (int)floor(i.pos.x);
    return CalibrationLuma(symbol).xxxx;
}
```

Use a 16x1 destination for this example. Raster position identifies the entry even though `_OutputWidth` and `_OutputHeight` were copied from the **byte** material. Do not use those copied byte-output dimensions to index the LUT.

The preparation shader must **not** compile the LUT keyword variant and must **not** include `TSMPDecodeByteOutput.cginc`. It always samples the original image and writes float calibration values. In particular, never read `_CalibrationLut` while rendering to it. No blending, sRGB conversion, mipmaps, Half conversion or RGBA8 byte packing belongs in this pass.

### 3. Keep both byte-decode variants

In the byte shader's `Properties`, declare `[HideInInspector] _CalibrationLut ("Calibration LUT", 2D) = "black" {}`. In its program, add:

```hlsl
#pragma multi_compile_local _ TSMP_CALIBRATION_LUT
```

Include the same calibration function, keep the existing `DecodeByte(int byteIndex)` implementation and then include `TSMPDecodeByteOutput.cginc`. The base preparation hook disables the keyword before each pass. Only a prepared LUT enables it again. Use `multi_compile_local`, not a variant that disappears because the keyword was off on the saved material.

The preparation and byte materials must be separate. The helper copies byte-material properties into the preparation material but does not replace its shader. Assign both materials on the codec prefab and keep their shader references in the package.

### 4. Validate the whole pair

Compare the original path with the preparation-plus-byte path, not just the cost of the final byte shader. Include tiny payloads: an extra Blit can outweigh the sampling saved. Luma4 uses the original path for effective sample size 1 and preparation for larger samples.

Compare exact decoded bytes on clean and perturbed colors, both Y orientations, automatic and explicit sample sizes, partial RGBA output, changing input and missing preparation material. Test the packaged prefab, disable/re-enable and multiple controllers. Build and run a Player to check that both local variants survive; an Editor-only test is insufficient.

LUT allocation is reused, but its contents are refreshed per enabled pass. `TSMPDecoder` provides its frozen input snapshot to both preparation and byte shaders. LUT preparation does not take another source snapshot and does not reuse calibration values across decode operations. Custom codec callers must keep their input image stable themselves.

## Shader include paths

Package shaders should use package-stable include paths. Relative include paths that depend on a scene folder are fragile when the package is moved between project and package locations.

Prefer includes that still work when the package is installed as a VPM package.

## Precision rules

- Keep header sampling exact.
- Avoid filtering on payload symbols.
- Use point sampling for byte recovery.
- Clear or overwrite every output pixel used by the current payload.
- Leave unused output pixels deterministic when possible to avoid visible stale blocks.

## Material properties

Custom codecs should document their shader properties. Common properties include:

| Property | Meaning |
| --- | --- |
| `_MainTex` | Source frame or payload texture. |
| `_PayloadByteTexture` | Intermediate byte texture. |
| `_PayloadSize` | Valid payload byte count. |
| `_BlockSize` | Codec block size. |
| `_SampleSize` | Decode sample size. |
| `_CodecOptionBytes` | Codec-specific option vector. |

Use stable property names so editor setup and runtime code can assign values without special cases.

## Validation

Before shipping a codec shader:

- Encode and decode a deterministic byte sequence.
- Test non-full payload sizes.
- Switch codecs and confirm no stale blocks remain.
- Test with capture scaling disabled.
- Test with the same render texture size users will ship.
