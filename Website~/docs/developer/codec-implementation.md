---
title: Codec Implementation Guide
---

# Codec implementation guide

This guide covers a full codec implementation: C# handler, encoder logic, decode shader, materials, package metadata, and validation.

Use this page after reading [Custom codec](./custom-codec.md). That page explains the package-level contract; this page explains the implementation details that usually cause bugs.

## Implementation order

Build a codec in this order:

1. Choose a stable `codecId` and symbol mode.
2. Implement capacity calculation.
3. Implement payload writing in C#.
4. Write a decode shader that can recover bytes from the written pixels.
5. Configure decode materials.
6. Register the codec prefab/catalog.
7. Validate byte-for-byte round trip before using real scene data.

Do not start with a complex shader. First make a tiny payload round-trip through one known frame.

## Codec pipeline

```text
TSMPEncoder
  -> builds header bytes and payload bytes
  -> queries TSMPCodec for symbol mode, start row, capacity, options
  -> passes payload bytes and pixel buffer to TSMPCodec
  -> TSMPCodec writes encoded pixels
  -> output texture is captured or transported

TSMPDecoder
  -> reads frame header
  -> validates CRC
  -> selects codec by codecId
  -> applies codec options
  -> runs codec decode material into a byte texture
  -> reads payload bytes and dispatches messages
```

The encoder does not know codec-specific classes. It calls the selected `TSMPCodec` through the base API. This keeps optional codec packages independent from Core.

## Package layout

Use a package layout that keeps runtime code, shaders, materials, prefabs, and samples together.

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

The package should depend on Core:

```json
"dependencies": {
  "com.kibalab.tsmp.core": "0.3.0-beta.2"
}
```

This example targets Core **0.3.0-beta.2**, currently a release candidate, not an already published version. Core 0.2.0 and 0.3.0-beta.1 do not contain the preparation API. Publish a dependent codec only after the matching Core is available and the packaged combination has been tested.

Use `"com.kibalab.tsmp.core": ">=0.3.0-beta.2"` in `vpmDependencies` too. UPM package dependencies use version strings, not ranges or Git URLs. For local/disk or Git installation, the **project** must explicitly supply a compatible Core too; this package manifest does not locate it on GitHub. VPM resolves the range from configured repositories; enable pre-release visibility when selecting betas.

Missing preparation materials only fall back after compilation. They cannot make a new codec compile against an old base class. Codecs not using the new API may keep their previously tested minimum version.

See [Unity package manifests](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-manifestPkg.html) and [VPM versions and ranges](https://vcc.docs.vrchat.com/vpm/packages/#versions-and-ranges).

## 1. Choose the symbol model

Define how many bits each encoded block carries.

Examples:

| Model | Meaning |
| --- | --- |
| 4-bit luminance | Two blocks per payload byte. |
| 8-bit color index | One block per payload byte. |
| N-bit RGB symbol | `ceil(payloadBits / N)` blocks. |

Pick:

- A stable symbol mode integer.
- A stable `codecId`.
- Whether the codec needs calibration blocks.
- Whether codec options are needed.

The frame header has up to five codec option bytes.

Keep `codecId` and symbol mode stable after release. Changing either value is a wire-format change for that codec.

## 2. Implement the C# handler

Create a `TSMPCodec` subclass.

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

Keep the runtime path UdonSharp-compatible. Avoid generic methods, LINQ, reflection, and unsupported Unity APIs.

## 3. Write encoder logic

The encoder-facing method is `WriteEncoderPayload`.

It receives:

| Parameter | Meaning |
| --- | --- |
| `pixels` | Writable output pixel or block buffer. |
| `width`, `height` | Output frame dimensions. |
| `blockSize` | Size of one encoded block. |
| `payloadBytes` | Payload buffer. |
| `payloadByteCount` | Number of valid payload bytes. |

Rules:

- Do not write outside the active frame area.
- Keep header/base regions intact.
- Write only the payload area your codec owns.
- Return `false` if required state is missing.
- Clear or ignore unused capacity consistently.

Useful helpers from `TSMPCodec`:

- `GetEncoderActiveWidthBlocks`
- `GetEncoderActiveHeightBlocks`
- `WriteEncoderColorBlockAtIndex`
- `ReadEncoderBits`

### Capacity must match the shader

The most common codec bug is a mismatch between these three values:

- Encoder payload capacity.
- Encoder payload start block.
- Decoder shader `_StartBlock` and `_ByteCount`.

If any of these disagree, the header can decode correctly while payload messages are corrupted or missing.

## 4. Add codec options

Use codec option bytes for decode choices such as bit depth, refinement mode, or calibration mode.

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

On decode, read them in `ApplyDecodeOptions()`:

```csharp
public override void ApplyDecodeOptions()
{
    int mode = ReadCodecOptionByte(0, 0);
    bool refine = ReadCodecOptionFlag(1, false);
}
```

## 5. Write a decode shader

Decode shaders convert encoded pixels back into byte pixels. The output is a byte texture where each RGBA pixel represents four decoded bytes.

The shader contract is:

```text
DecodeByte(byteIndex) -> integer 0..255
```

`TSMPDecodeByteOutput.cginc` handles packing four decoded bytes into one output pixel. Your shader only needs to implement byte recovery for the codec's symbol representation.

Start from this structure:

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

`TSMPDecodeCommon.cginc` provides:

- Source texture sampling.
- Block sampling.
- `PayloadBlockIndex`.
- YCoCg helpers.
- Flip-Y handling.

`TSMPDecodeByteOutput.cginc` calls `DecodeByte(byteIndex)` four times per output pixel and writes the RGBA byte texture.

### Include paths

Use package include paths for shared TSMP shader files. This keeps codec shaders independent from the project `Assets` layout.

### Shader properties

Your decode material should support the standard properties used by `TSMPCodec.ConfigureDecodeMaterial`:

| Property | Meaning |
| --- | --- |
| `_MainTex` | Source TSMP texture. |
| `_BlockSize` | Encoded block size in pixels. |
| `_SampleSize` | Decode sample size from the header/setup. |
| `_StartBlock` | First payload block to decode. |
| `_ByteCount` | Number of payload bytes requested. |
| `_ActiveWidthBlocks` | Active block width of the frame. |
| `_SourceWidth` / `_SourceHeight` | Source frame dimensions. |
| `_OutputWidth` / `_OutputHeight` | Byte texture dimensions. |
| `_FlipY` | Whether source sampling should flip vertically. |

## 6. Add calibration if needed

If the transport changes colors, add calibration symbols before payload blocks.

Typical pattern:

1. Encoder writes a known symbol table.
2. Decode shader samples the calibration table.
3. Decode shader classifies payload blocks by nearest calibration entry.

Expose the calibration start block through material properties when needed. Use `ConfigureMaterials(CodecMaterialContext context)` for editor/native paths and `ApplyDecodeOptions()` for runtime paths.

## Optional calibration preparation

Calibration means measuring the known reference symbols in the received image, then comparing payload samples with those measured values. Reading the same reference blocks for every output byte repeats work. A lookup table (LUT) can move that work into one GPU preparation pass.

The LUT changes where calibration is calculated, not the packet format or the classification algorithm. A codec that does not benefit from this extra pass should keep its original decode path.

### Call order

For **each header or payload byte pass**, the decoder:

1. Selects the handler and calls `ApplyDecodeOptions()` to choose the byte material and layout.
2. Sets `_MainTex`, source dimensions, block/sample size, `_StartBlock`, `_ByteCount`, byte-output dimensions and `_FlipY` on that material.
3. Calls `PrepareDecode(source, material)`. An override may prepare calibration from those exact settings.
4. Immediately blits the same source Texture reference through the byte material.
5. Requests readback from the byte output, not from the LUT.

Do not prepare in `ApplyDecodeOptions()`: the decoder has not yet assigned all properties for this pass. Do not prepare only in `Start()`, on a codec change, or once per frame. Header and payload passes can use different settings and run at different times.

The shared Texture reference is **not an immutable pixel snapshot**. A video producer can update its pixels while the decoder waits for header readback. Preparation does not freeze the image or guarantee that header and payload came from the same video frame.

### Minimal Luma4 hook

The Luma4 handler uses the following override. It belongs inside the existing codec class, alongside its encode methods and `ApplyDecodeOptions()`.

```csharp
public override void PrepareDecode(Texture source, Material material)
{
    base.PrepareDecode(source, material);
    if (material == null || GetDecodeSampleSize(material) <= 1)
        return;

    PrepareCalibrationLut(source, material, 16);
}
```

Luma4 has 16 reference symbols, so the table is 16 by 1. Effective sampling of one retains the original path because the preparation overhead outweighed the saved sampling in the measured configuration. This is a Luma4 policy, not a rule Core applies to other codecs. Measure your own codec, including preparation cost, before choosing a condition.

Assign a material using the Luma4 preparation shader to the inherited `calibrationMaterial` field on the codec prefab. This field is hidden from the ordinary Inspector; configure the serialized prefab field through your package's editor tooling or Inspector Debug mode. End users should receive an already configured catalog/prefab, not an extra setup step.

The byte shader also needs `_CalibrationLut`, the `TSMP_CALIBRATION_LUT` local variants and the original sampling fallback. The [shader guide](./codec-shaders.md) shows their implementation. Adding only the C# override does not add LUT support to a shader.

### Resource ownership and fallback

- The codec base owns the generated LUT RenderTexture. It reuses the allocation, redraws its contents before each enabled pass, and releases/destroys it on disable or destruction.
- `calibrationMaterial` and the byte material are assigned references, not materials the hook creates. The hook changes their properties. Setup prepares controller-owned material copies; a custom integration must also prevent conflicting use of shared materials.
- Always call `base.PrepareDecode(source, material)` before testing your enable condition. This disables the old LUT keyword, including when the same material now needs the ordinary path.
- If you override `OnDisable()` or `OnDestroy()`, call the corresponding base implementation. Do not destroy package material assets or an externally supplied source/output texture.
- Missing source, material, preparation material, invalid LUT width or empty byte output skips preparation. Native checks also reject unsupported preparation shaders/ARGBFloat devices; both paths check the resulting format and texture creation. Keep the original shader variant available.
- Use one-row, linear `ARGBFloat` with 32-bit float channels, point filtering and no mipmaps. Do not substitute Half, 8-bit or sRGB storage: rounding can change which symbol wins near a classification boundary.
- If there are multiple preparation passes, define their order and fully overwrite each target. Never sample a LUT while writing into that same texture.

For method signatures, guards and lifecycle behavior, see [TSMPCodec Scripting API](../scripting-api/codec.md#runtime-decode-preparation). Validate missing-resource fallback, changing sample sizes, disable/re-enable, multiple codec instances and both Player shader variants with byte-for-byte comparisons.

## 7. Configure materials

Create a material for each decode shader and assign it to your codec component.

For native/editor material configuration:

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
    SetFloatIfPresent(byteDecodeMaterial, "_MyCalibrationStartBlock", Luma4Raster.PayloadStartRow * context.FrameLayout.ActiveWidthBlocks);
}
#endif
```

For runtime decode, `ApplyDecodeOptions()` must set the selected material and runtime fields:

```csharp
public override void ApplyDecodeOptions()
{
    selectedDecodeMaterial = byteDecodeMaterial;
    payloadStartRow = 5;
    payloadBlockCount = byteCount;
}
```

## 8. Implement editor/native frame writing

If your codec should encode in editor tools or native C# paths, implement `TryWriteFrame`.

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

This path is separate from UdonSharp runtime encoding but should produce the same frame layout.

## 9. Register the codec

To make it appear in `TSMPSetup`:

1. Create a prefab with your codec component.
2. Assign `codecId`, `displayName`, materials, and options.
3. Add a `TSMPCodecCatalog` asset that references the prefab.
4. Put package metadata in `package.json`.
5. Refresh codecs in `TSMPSetup`.

The encoder should never hard-code your codec type. It should call it through `TSMPCodec`.

## 10. Validate the codec

Minimum validation:

- Header bytes decode correctly.
- Payload bytes round-trip exactly.
- Payload capacity calculation matches real writable capacity.
- Payload start row matches shader `_StartBlock`.
- Codec option bytes arrive on the decoder.
- CRC mismatch is reported when header pixels are corrupted.
- Output is cleared when payload shrinks.
- Unity editor path and UdonSharp runtime path produce compatible frames.

Recommended first payload:

```text
00 01 02 03 04 05 06 07 08 09 FE FF
```

This catches byte order, clamping, zero handling, and high-value handling before testing real TSMP payloads.

## Common failure modes

| Symptom | Likely cause |
| --- | --- |
| Header valid but payload invalid | Payload start row or byte capacity mismatch. |
| CRC mismatch | Header region is damaged or shader reads wrong pixels. |
| Works in editor, fails in Udon | Runtime path uses unsupported API or differs from native path. |
| Old data remains visible | Output clearing or unused-region handling is incomplete. |
| Decoder reads too few bytes | `payloadBlockCount` or `_ByteCount` is wrong. |
| Codec does not appear in setup | Missing catalog, prefab reference, or package metadata. |
