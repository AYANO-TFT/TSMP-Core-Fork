---
title: Frame and Payload Algorithms
---

# Frame and payload algorithms

This page describes the runtime data flow at a high level. It is useful when reviewing encoder/decoder changes, adding tests, or debugging why a frame contains fewer messages than expected.

## Encoder frame flow

```text
EncodeNow
  -> collect active TSMPNetworkBehaviour targets
  -> begin network payload
  -> for each behaviour:
       call TSMPBeforeEncode
       write enabled TransSync fields
       cancel empty VariableState messages
  -> write queued RPC messages, if any
  -> patch message count
  -> write frame header with CRC
  -> ask codec to draw header and payload into output texture
```

Important behaviour:

- Empty `VariableState` messages are rolled back.
- RPC-only frames are allowed.
- Queued RPCs can repeat for multiple frames.
- Payload copy and FEC are not part of the current wire format.
- The selected codec supplies payload start row, capacity, symbol mode, and option bytes.

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

The decoder matches variable hash and network ID to a generated binding entry.

Encoder rule: if no variables were written for a behaviour, the message is canceled and the message count is not increased.

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

The decoder resolves the method name through payload data and dispatches the event through `TSMPBehaviour.SendCustomEvent`.

RPC events are repeated by the encoder for a small number of frames to tolerate texture frame loss.

## Header validation

Decoder validation order:

1. Buffer range.
2. Magic.
3. Header size.
4. Major version.
5. Header CRC32.

CRC is calculated over header bytes `0..51`. The stored CRC is at bytes `52..55`. A mismatch discards the frame before payload processing.

## Decode flow

```text
Update / decode tick
  -> capture input into the decoder-owned snapshot
  -> read header area from the snapshot
  -> validate header and CRC
  -> choose codec by codecId
  -> request payload bytes from the same snapshot according to PayloadSize
  -> decode network frame header
  -> iterate messages
  -> apply variable values or dispatch RPC calls
  -> update diagnostics
```

Malformed payload data stops further processing and records diagnostics. This does not roll back variables or RPC effects already applied earlier in the payload; whole-frame transactional application is a separate issue.

### GPU preparation within each byte pass

Both the header pass (Luma4) and the selected payload codec use this sequence:

```text
ApplyDecodeOptions
  -> configure byte material for this pass
  -> PrepareDecode(source, byteMaterial)
       -> disable the previous LUT keyword
       -> optionally sample reference symbols into a float LUT
       -> bind the LUT and enable its byte-shader variant
  -> byte Blit from the same snapshot as preparation
  -> GPU readback of recovered bytes
```

Without a LUT, the byte shader repeatedly samples reference blocks while classifying payload symbols. With a LUT, those reference samples are calculated once per enabled pass; payload sampling and classification remain unchanged. The extra pass is useful only when its cost is lower than the repeated work it removes. Measure preparation plus decoding for both small and large payloads.

Luma4 prepares 16 entries when the effective sample size exceeds one; single-sample decoding retains the original path. The generated texture is linear ARGBFloat (32-bit float per channel), point-filtered, with no mipmaps. Half/8-bit quantization can change classification at boundaries and is not part of this algorithm.

The texture allocation is reused, not its values across frames: contents are redrawn for every enabled byte pass. Missing resources keep the original shader path. Each codec owns its generated LUT and releases it on disable/destruction; material ownership is handled separately.

Before the header pass, `TSMPDecoder` captures the source into a reusable, same-size, linear `ARGBFloat` RenderTexture. Every header, calibration and payload pass reads this snapshot until the operation finishes. A producer may update or replace the original input during readback without mixing image generations. The snapshot is resized between operations when necessary and released on disable/destruction. Disabling cancels the operation; its outstanding callback must drain before another decode can start. Capture failures reject the operation rather than falling back to a changing source.

The copy adds one full-image GPU Blit per decode attempt and 16 bytes per pixel of snapshot storage; no extra CPU readback or wire-format field is added. Float32 avoids adding an 8-bit or half-float quantization step to sampled source values. See the [decoder API](../scripting-api/decoder.md#input-snapshot) for ownership and resource requirements. This does not make a producer's already-corrupted image valid or make variable/RPC application transactional.

See the [implementation guide](./codec-implementation.md), [shader guide](./codec-shaders.md), and [preparation API](../scripting-api/codec.md#runtime-decode-preparation) for the hook, material setup, lifecycle and fallback contract.

## Where to add tests

Add protocol tests around:

- Header CRC generation and rejection.
- Message count patching.
- Empty variable message rollback.
- RPC-only frames.
- Payload capacity boundaries.
- Duplicate frame and duplicate RPC handling.
