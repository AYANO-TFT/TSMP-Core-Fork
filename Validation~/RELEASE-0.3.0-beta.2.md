# TSMP Core 0.3.0-beta.2

This prerelease consolidates the changes since 0.3.0-beta.1. The stable release remains 0.2.0.

## Component Send Mode

Each TSMPNetworkBehaviour now has a shared Send Mode dropdown:

- **Default** uses the component author's per-field TransSync settings.
- **On Change** sends changed values and periodically refreshes unchanged values.
- **Always** also sends unchanged values, allowing stationary Transform and paused Timeline state to be repeated more often at the cost of bandwidth.

Changes take effect without rebuilding bindings. Minimum send intervals, field eligibility, priority and available payload capacity still apply. Always does not guarantee delivery or increase priority. RPCs and manual Writer calls are unaffected.

## Decoder and Synchronization Fixes

- Freeze each decoded frame into one reusable Float32 snapshot so header, calibration and payload reads cannot mix different source images.
- Reuse payload buffer capacity while parsing only the current valid bytes; reject empty or truncated NetworkFrame payloads before payload readback.
- Filter duplicate and older frames with wrap-aware ordering. Add receiver-local Window Size, default 256, for accepting frame zero after a sufficiently large previous index.
- Honor Receive Interpolation None before decoding field values; clear pending Rigidbody velocities when physics reception is disabled.
- Invalidate stale BlendShape interpolation targets and honor the receiver's Animator layer selection.
- Correct looping/non-looping Animator drift handling and negative state-hash conversion in Udon.
- Retire avatar pool assignments above a reduced Max Players limit while retaining pool objects and rigs for later reuse.
- Commit avatar root delta and keepalive state only after successful frame output through TransSync.SentEvent.

## Encoder, Streaming and Codec API

- Fix RPC queue accounting for reentrant sends and reject events that cannot fit the configured payload. RPC retry counts remain a finite attempt budget, not a delivery guarantee.
- Remove Setup's redundant Editor output copy; only the encoder presents successful frames.
- Preserve integer-sized blocks and clear remainder pixels when expanding output textures.
- Pace FFmpeg output independently of incoming updates while keeping the latest frame.
- Add codec-owned PrepareDecode, GetDecodeSampleSize and calibration-LUT helpers. Core still does not select codec-specific calibration algorithms. Updated codecs using these APIs require Core 0.3.0-beta.2; older codecs can keep the default no-op preparation path.
- Update English, Korean and Japanese documentation for the APIs, Send Mode and installation compatibility.

## Wire Compatibility

The protocol version and datagram remain unchanged: a 56-byte header followed by the existing payload. No Send Mode, window or session bytes are added.

| Byte range | Field | Encoding |
| --- | --- | --- |
| 20..23 | StreamId | UInt32 little-endian |
| 24..27 | FrameIndex | UInt32 little-endian |
| 28..31 | TimestampMs | UInt32 little-endian; not used for ordering |
| 44..49 | Reserved | Six zero bytes |
| 52..55 | Header CRC32 | UInt32 little-endian over bytes 0..51 |

The existing skipDuplicateFrames setting now filters older frames as well. Frame zero is accepted when the previously applied index is at least Window Size. This is a restart heuristic, not session identification: delayed old zero frames, a missing reset frame and early restarts remain ambiguous. RPC deduplication is unchanged.

## Validation

Recorded 2026-09-18 with Unity 2022.3.22f1, Windows x64, NVIDIA RTX 4090 and Direct3D11. Native texture tests used Luma4 0.0.4-beta.1 from the local codec repository. SDK checks used Worlds 3.10.4-beta.2 and its bundled UdonSharp. The Player used Development Mono with managed stripping disabled.

| Check | Result |
| --- | --- |
| SDK-free TransSync scheduling and live Send Mode | Passed, 14 cases |
| SDK Editor scheduling and proxy values | Passed, 13 cases |
| Compiled Encoder execution in the Udon VM | Passed |
| Native and SDK Editor encoding | Passed |
| Native Gamma Play Mode GPU loopback | Passed, 11 decoded frames |
| Existing Luma4 0.0.3 compatibility, native Gamma Play Mode | Passed, 11 decoded frames |
| Windows x64 Mono Player build | Succeeded, zero errors, 16 existing shader warnings |
| Actual Gamma Player GPU loopback | Passed, 11 decoded frames |
| Fresh full UdonSharp client compilation and Controller binding setup | Passed, 16 TSMP programs |
| Local VRChat SDK world bundle build | Passed, 553352-byte bundle |

The GPU loopback covers Transform, animated Humanoid, Timeline, Unicode fields and RPC delivery, including recovery from an intentionally skipped stationary-state frame. Package archive creation and VPM registration are separate distribution checks, not runtime verification.

Scheduling, Editor and Player evidence is recorded in [Component Send Mode Validation](https://github.com/kibalab/TSMP-Core/blob/v0.3.0-beta.2/Validation~/TransSync/COMPONENT-SEND-MODE.md). Local evidence roots:

- `F:/Unity/TSMP/Validation-Results/component-send-mode/`
- `F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.2/20260918-200606-Udon.log` and adjacent `-result.txt`
- `F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.2/20260918-200714-World.log` and adjacent `-result.txt`
- `F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.2/stable-luma4/20260918-201509-Play.log` and adjacent `-result.txt`, using the local Luma4 0.0.3 release checkout in `F:/Unity/TSMP/Validation-NoSDK`

Player artifact: `F:/Unity/TSMP/Validation-Results/issue22-20260913/After/Build/Mono/TSMPValidation.exe`, with an adjacent build report. SDK bundle: `C:/Users/kjh03/AppData/LocalLow/VRChat/VRChat/Worlds/scene-standalonewindows64-world-8e8595cc-7165-4ead-9c2e-3fb940b5a22f.vrcw`.

The native project references the actual Core checkout. The SDK project uses an isolated embedded copy; all 100 Core C# sources match the release checkout after normalizing CRLF line endings. To repeat the final SDK checks, run Validation~/Run-Validation.ps1 against the prepared VRC validation project with Step Udon, then Step World. No world was uploaded. English, Korean and Japanese website builds and the TypeScript check also passed after finalizing the release documentation.

## Known Limitations

- The full legacy Linear loopback harness failed at its CPU raster-reader/block-expansion calibration check: `Calibration symbols 0 and 1 are too close.` The Gamma loopback and separate Linear Editor encoding tests passed. This release does not claim the full Linear suite passes.
- Live VRChat client execution, built-world reference behavior, Quest, IL2CPP and managed stripping were not verified. Successful Udon VM tests and SDK bundle generation do not establish those results.
- The reviewed proxy/backing-reference risks were not changed in this release. In particular, Component-typed proxy references and fallback resolution with multiple UdonBehaviours still require real-client validation; they are not listed as fixed.
- Codec preparation performance changes in separate codec repositories are not automatically included in this Core package. Install matching codec releases separately when available.
