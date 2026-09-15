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
