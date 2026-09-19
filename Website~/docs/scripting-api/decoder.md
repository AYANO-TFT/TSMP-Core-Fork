---
title: TSMPDecoder API
---

# `TSMPDecoder`

## Combined byte output

`useCombinedByteOutput` defaults to `true`. In the predicted path, compatible codec shaders write the 56-byte decoded header and payload directly into the private RGBA8 readback target, removing the separate packing pass and intermediate payload write. The immutable source snapshot, header CRC validation, exact-length prediction check and same-snapshot fallback are unchanged. This does not change the wire datagram or guarantee a delivery rate.

Shaders opt in by implementing both `_TSMPHeaderTex` and `_TSMPHeaderPixels`. Existing codecs without these properties keep the separate packing path. The switch is in Diagnostics > Advanced Decode. `combinedByteOutputCount` counts submissions, including predictions later rejected or retried; it is not a received-frame count. `ResetDecodeDiagnostics()` resets it.

`TSMPDecoder` reads a TSMP texture, validates the header, asks the selected codec to recover payload bytes, and applies variable/RPC messages to bound behaviours.

Use this page when wiring a custom receiver, reading diagnostics, or debugging why a frame was ignored.

## Component fields

| Field | Purpose |
| --- | --- |
| `sourceTexture` | Texture that contains the encoded TSMP frame. |
| `payloadByteTexture` | Render texture used by codec shaders when decoding payload bytes. |
| `codecHandlers` | Codec components available to the decoder. The header `codecId` selects the handler. |
| `applyEveryFrame` | Runs decode from the component update loop. Disable it when another script calls `DecodeNow()`. |
| `skipDuplicateFrames` | Enables duplicate and older-frame filtering for the current stream. Inspector label: Filter Frames. |
| `frameWindowSize` | Window Size in frames, default 256, minimum effective value 1. Used for the frame-zero restart exception; not a buffer allocation. |
| `flipY` | Flips texture sampling for capture paths that invert the image. |
| `useHeaderPayloadLayout` | Uses header payload metadata to size readback. Keep enabled for normal use. |
| `payloadBytesOverride` | Manual payload byte count for test paths. |
| `decodeSafetyMode` | Extra guards for malformed or partial frames. |
| `usePredictedReadback` | Default true. Use the previous validated configuration to attempt a combined header/payload readback. Manual layout and safety modes bypass it. |

`TSMPSetup` normally assigns `sourceTexture`, byte textures, codec handlers, and binding arrays.

## Header validation

The decoder rejects a frame before applying payload messages when:

- Magic bytes are not TSMP.
- Header size is not supported.
- Major protocol version is not supported.
- Header CRC does not match.
- Payload size exceeds the available texture layout.

CRC failures are logged as warnings. This helps identify capture corruption without applying bad data.

## `DecodeNow()`

```csharp
public void DecodeNow()
```

Starts decoding one captured frame asynchronously. It is safe to call with `applyEveryFrame` disabled, but the component must remain enabled. Calls while a readback is pending do not start another operation.

The initial/fallback path performs this order:

1. Copy the current input image into the decoder's snapshot.
2. Read the header pixels from that snapshot.
3. Validate magic, version, payload layout, and CRC.
4. Select a codec handler by `codecId`.
5. Decode payload bytes from the same snapshot.
6. Parse network messages.
7. Apply variables and dispatch RPC calls.

## Predicted readback {#predicted-readback}

After learning a valid header, the decoder can submit a Luma4 header pass and a payload pass using the previous configuration without waiting for the header on the CPU. It packs the decoded bytes into a private linear RGBA8 target and submits one readback. The original snapshot remains Float32; only already-decoded integer bytes use RGBA8.

The current header must pass magic, version, size and CRC validation, then the normal frame-order filter. All header bytes before CRC must match the cached header except frame index, timestamp and payload length. Payload length is then checked separately for **exact equality**, and resolved block/sample/start-block settings must match. A length change in either direction, codec/option/stream/layout change or other mismatch discards the speculative payload and requests the actual payload from the **same snapshot**. No second source capture occurs. CRC failure rejects the frame instead of falling back to unchecked data.

Exact-length prediction avoids assuming custom codecs produce the same prefix when asked to decode more bytes. Existing codec interfaces and shaders are unchanged; each pass still calls `PrepareDecode` with its current snapshot. A header must be learned again after disable, source dimension/orientation changes, or use of manual layout/safety modes. Missing packing resources retain the sequential path. The serialized packing material is populated automatically in the Editor/build preparation; ordinary Unity runtime creation also loads the package resource.

The internal readback buffer contains header bytes `0..55`, then the predicted payload at offset `56`, followed only by row padding. This is **not a wire-format change**. Extra GPU storage is a 14x1 header target and a packed target with width `min(256, ceil((56 + payloadBytes)/4))` and enough rows, four bytes per pixel. Owned targets are reused and released on disable/destruction. Only one captured image remains in flight.

`predictedReadbackCount` counts accepted speculative payloads, not successful RPC executions. `predictionFallbackCount` counts valid-header prediction mismatches that trigger fallback. Both appear in Runtime Status. Duplicate/older frames may already have incurred speculative GPU work, but their payload is not parsed or dispatched. This optimization does not guarantee 60 Hz delivery; measure applications, latency and RPC events independently.

## Input snapshot {#input-snapshot}

No additional Inspector reference or mode is required. The decoder owns a same-size linear `ARGBFloat` RenderTexture, reuses it between operations, and releases it on disable/destruction. This preserves sampled input precision without an extra 8-bit or half-float quantization step. It requires Float32 render-target support; allocation/format failures reject the decode and report `lastError` through the decoder's error logging policy. There is no fallback to a changing source image.

The extra storage is `width * height * 16` bytes: about 3.52 MiB at 640x360, 31.64 MiB at 1920x1080, or 126.56 MiB at 3840x2160. Each accepted decode attempt copies the full input once, including attempts later rejected by CRC or duplicate checks. There is no added CPU readback. Measure the GPU copy cost on the deployment hardware.

Changing or destroying `sourceTexture` after capture affects the next operation, not the current image. Disabling the decoder cancels its pending operation; a callback received after re-enable is discarded before a new decode can start. Header, LUT preparation and payload use the same snapshot in both native and Udon paths. Custom codecs must treat the supplied image as read-only and must not retain it as a permanent frame copy. This does not address out-of-order frames or transactional variable/RPC application.

## Frame window and ordering {#frame-window}

With filtering enabled, compare the incoming index `N` with the last successfully applied index `P` of the current stream. The effective window is `W = max(1, frameWindowSize)`; changing it takes effect on the next header decision.

1. With no previously applied frame, or a different `StreamId`, accept the candidate without an order comparison.
2. If `N == P`, skip the duplicate.
3. Calculate `D = (N - P) modulo 2^32`. If `0 < D < 2^31`, accept it as newer, including natural counter wrap.
4. Otherwise, accept `N == 0` when `P >= W` as an inferred restart.
5. Skip other candidates as older/ambiguous. Exactly half the UInt32 range is ambiguous unless the zero restart exception applies.

Acceptance here permits payload processing, not unconditional application. Only a successful network-frame application advances the remembered stream/index. A corrupt payload, cancelled operation or header/readback-only safety test does not establish a new baseline. Duplicate and older frames retain the existing `lastFrameValid=true` skip convention; they are counted separately by `skippedDuplicateFrameCount` and `skippedOutOfOrderFrameCount` and have a descriptive `lastError` status. They do not parse or dispatch payload messages, even if speculative byte decoding/readback has already completed.

For `W=256`: `100 -> 101 -> 100` skips the final 100; `255 -> 0` skips zero; `256 -> 0 -> 1` accepts the restart and continuation. `4294967295 -> 0` is a normal forward wrap regardless of the window. With `W=512`, `256 -> 0` is skipped. The window is a frame-count threshold, not a time interval, a sliding replay cache or a group of stored textures.

This is deliberately a heuristic without a session ID. A delayed old zero can trigger a false restart; a restart that loses frame zero or occurs before one window has elapsed is not recognized by this exception. After an accepted reset, sufficiently high old frames can again appear newer. Changing streams accepts the candidate and replaces the baseline after successful application; no history of retired streams is retained. RPC event deduplication is unchanged and is not cleared by this heuristic. Disabling `skipDuplicateFrames` bypasses both ordering checks and the restart rule, useful for intentional recorded-video seeking but allowing old variable values to be applied.

### Datagram compatibility

The datagram and protocol version are **unchanged**. The header is still 56 bytes and the payload format is unchanged. `StreamId` occupies bytes `20..23` (UInt32 little-endian), `FrameIndex` bytes `24..27` (UInt32 little-endian), and `TimestampMs` bytes `28..31` (UInt32 little-endian, not used by this rule). Reserved bytes `44..49` remain zero. Header CRC32 remains at `52..55` over bytes `0..51`. Neither the window size nor a session ID is transmitted. See the [frame layout](../concepts/protocol.md) for the remaining fields. Existing senders require no change; the receiver's frame-admission policy changes.

## `ResetDecodeDiagnostics()`

```csharp
public void ResetDecodeDiagnostics()
```

Resets the error-log budget and the predicted-readback/fallback counters. It does not reset the existing frame/RPC counters or the last-applied stream/frame used by the ordering rules.

## Diagnostics

| Member | Meaning |
| --- | --- |
| `lastFrameValid` | Last frame passed all decode stages. |
| `lastHeaderValid` | Header was valid before payload decode. |
| `lastError` | Last decoder error or warning text. |
| `lastNetworkMessageCount` | Number of messages found in the decoded network frame. |
| `lastAppliedVariableCount` | Number of variables applied to behaviours. |
| `lastRpcCallCount` | Number of RPC messages dispatched. |
| `lastCodecId` | Codec ID read from the last valid header. |
| `lastFrameIndex` | Frame index read from the header. |
| `lastPayloadBytes` | Payload byte count used by the decoder. |

## Binding application

The decoder uses binding arrays generated by `TSMPSetup`. Each binding maps:

- `networkId`
- `variableHash`
- value type
- target behaviour
- target field name
- sync direction

When a variable message arrives, the decoder only applies it to matching bindings. When an RPC message arrives, the decoder dispatches it to the matching `TSMPNetworkBehaviour`.

## Runtime rules

- A frame with a valid header but unknown codec ID is ignored.
- `ReceiveInterpolation.None` on a target behaviour means received values are ignored for that behaviour.
- RPC calls are event-like. Use repeat frames on the encoder if the transport drops frames.
- Decoder logs important runtime failures because inspector fields are not visible in uploaded worlds.
