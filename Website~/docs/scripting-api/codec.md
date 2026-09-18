---
title: TSMPCodec
---

# TSMPCodec

Namespace: `K13A.TSMP`

Base type for codec handlers.

A codec handler tells the encoder how to write payload bytes into pixels and tells the decoder which material/options should be used to recover payload bytes.

## Public fields

| Field | Type | Use |
| --- | --- | --- |
| `codecId` | `ushort` | Stable codec ID written into the frame header. |
| `displayName` | `string` | Editor-facing name. |
| `codecOptionBytes` | `byte[]` | Decoder-side option bytes copied from the frame header. |
| `selectedDecodeMaterial` | `Material` | Material used for byte decode. |
| `calibrationMaterial` | `Material` | Optional preparation material assigned on the codec prefab; hidden in the ordinary Inspector. |
| `payloadStartRow` | `int` | First payload row for decode. |
| `payloadBlockCount` | `int` | Number of payload blocks to decode. |
| `byteCount` | `int` | Requested payload byte count. |

Runtime encoder query result fields are public for Udon event bridge use. They are assigned by `OnTSMPEncoderQuery()` and `OnTSMPEncoderWritePayload()`.

## Runtime encode methods

| Method | Use |
| --- | --- |
| `GetEncoderSymbolMode()` | Return symbol mode for frame header. |
| `GetEncoderPayloadStartRow(width, blockSize)` | Return first payload row. |
| `GetEncoderPayloadCapacityBytes(width, height, blockSize)` | Return payload byte capacity. |
| `GetEncoderCodecOptionByteCount()` | Return option byte count, max 5. |
| `GetEncoderCodecOptionByte(index)` | Return one option byte. |
| `WriteEncoderPayload(...)` | Write payload bytes into encoder pixels. |
| `ApplyDecodeOptions()` | Apply decoder state from header/options. |

These methods must be UdonSharp-compatible if the codec should run inside VRChat.

## Helper methods

| Method | Use |
| --- | --- |
| `ReadCodecOptionByte(index, fallback)` | Read one codec option with fallback. |
| `ReadCodecOptionFlag(index, fallback)` | Read one option as a boolean. |
| `GetEncoderActiveWidthBlocks(width, blockSize)` | Calculate writable block width. |
| `GetEncoderActiveHeightBlocks(height, blockSize)` | Calculate writable block height. |
| `WriteEncoderColorBlockAtIndex(...)` | Fill one encoded block with a color. |
| `ReadEncoderBits(...)` | Read arbitrary bits from payload bytes. |

## Editor/native methods

Available outside `COMPILER_UDONSHARP`:

| Method | Use |
| --- | --- |
| `SymbolMode` | Native symbol mode property. |
| `TryWriteFrame(...)` | Write complete frame into a `Texture2D`. |
| `GetCodecOptionBytes()` | Native codec option byte array. |
| `DecodeMaterialCount` | Number of decode materials. |
| `GetDecodeMaterial(index)` | Decode material lookup. |
| `DebugMaterialCount` | Number of debug materials. |
| `GetDebugMaterial(index)` | Debug material lookup. |
| `ConfigureMaterials(context)` | Configure decode/debug materials. |

## Udon event bridge

`TSMPCodec` exposes public event methods used by the encoder:

| Method | Purpose |
| --- | --- |
| `OnTSMPEncoderQuery()` | Populates encoder query result fields. |
| `OnTSMPEncoderWritePayload()` | Calls `WriteEncoderPayload` and stores the result. |

The encoder uses this bridge so optional codec packages can be called without hard-coding codec classes.

The Udon Encoder queries once per encoding attempt, so changing options on the same codec instance updates its capacity, payload row and header options together. Getters should be inexpensive and side-effect-free. A query result is reused within that attempt, not across subsequent encodes.

`OnTSMPEncoderQuery()` also populates `encoderQueryValues`, a reused `int[10]`: codec ID, symbol mode, payload start row, capacity in bytes, option count, then five option bytes. The bridge reads this array in one call. Treat it as a read-only result that changes on the next query, not as persistent configuration. Existing individual result fields remain available; custom codecs continue to override the same getters and need no codec-specific invalidation API.

## Runtime decode preparation {#runtime-decode-preparation}

`PrepareDecode(Texture source, Material material)` is called immediately before each header or payload byte pass, after the decoder has assigned the source dimensions, sample size, byte count and layout. `TSMPDecoder` supplies its owned input snapshot to this hook and the following byte Blit, keeping header and payload on the same captured image. Do not modify or release the supplied texture. Callers outside `TSMPDecoder` must capture or otherwise stabilize their own input. Override the hook for optional GPU preparation, and call `base.PrepareDecode(source, material)` first to clear the previous LUT keyword. Existing codecs need no override.

The protected `GetDecodeSampleSize(material)` helper resolves automatic sampling and clamps it to the block size, matching the decode shader. `PrepareCalibrationLut(source, material, width)` renders the assigned `calibrationMaterial` into a reusable, one-row, linear `ARGBFloat` texture. It copies the byte material's properties to the preparation material, binds the result as `_CalibrationLut`, and enables the local `TSMP_CALIBRATION_LUT` keyword. Declare both shader variants with `#pragma multi_compile_local _ TSMP_CALIBRATION_LUT` so they survive Player builds.

Assign the preparation material on the codec prefab. Its shader must write every LUT texel and must not read the LUT being written. Keep an ordinary decode variant for missing resources, unsupported Float32 allocation, empty output and modes where the extra pass is slower. Do not quantize calibration values to Half or 8-bit: this can change decoded bytes. The LUT is refreshed per pass, not reused across frames.

The base class releases its LUT on disable and destruction. If a codec overrides either lifecycle method, call the base implementation. Direct callers of the preparation hook must set the same material properties as the decoder and perform the byte blit immediately afterwards.

| Member | Contract |
| --- | --- |
| `public virtual void PrepareDecode(Texture source, Material material)` | Per-pass hook; no return value. The base disables LUT use and clears its previous material binding when the material changes. |
| `protected int GetDecodeSampleSize(Material material)` | Requires a non-null material. `_SampleSize > 0.5` uses its floored value; otherwise the default is 4 for blocks at least 8 pixels, or 3. The result is clamped to `1..min(blockSize, 8)`. |
| `protected void PrepareCalibrationLut(Texture source, Material material, int width)` | Requires the base hook first. `width` counts LUT entries, not byte-output pixels. Allocates/reuses `width x 1`, copies material properties, draws the preparation shader, binds `_CalibrationLut` and enables the keyword only after successful texture creation. |
| `protected virtual void OnDisable()` / `OnDestroy()` | Clear the owned LUT binding, release and destroy the generated texture. Do not destroy the assigned materials. |

Missing inputs, non-positive width or `_ByteCount`, a non-ARGBFloat result, or failed texture creation leaves the ordinary shader path selected when the base hook was called. Native compilation additionally checks preparation shader support and `SystemInfo.SupportsRenderTextureFormat`; those checks are not compiled into Udon. This is not a guarantee that a shader with an implementation error will recover automatically.

The material arguments and `calibrationMaterial` are mutable, externally assigned references. The hook does not clone them. Setup owns its prepared material copies; independent integrations must manage material sharing and lifetime themselves. Allocation reuse does not mean reuse of calibration values across passes.

See the [implementation guide](../developer/codec-implementation.md) for the Luma4 hook and the [shader guide](../developer/codec-shaders.md) for preparation output, keyword variants and LUT reads.
