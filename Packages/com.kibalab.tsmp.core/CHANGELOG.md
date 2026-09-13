# Changelog

## 0.3.0-beta.2 (Unreleased)

### Codec API

- Add `TSMPCodec.PrepareDecode(Texture, Material)` before each header/payload byte pass, with codec-owned linear Float32 calibration LUT allocation, per-pass refresh and lifecycle cleanup. Existing custom codecs can retain the default no-op preparation path.
- Provide `GetDecodeSampleSize`, `PrepareCalibrationLut` and the optional `calibrationMaterial` field for adaptive codec preparation without codec-specific branches in Core.
- Preserve packet layout, codec IDs and legacy shader paths. New codec sources that use the preparation API require this Core version; missing preparation materials fall back to ordinary decoding but cannot compensate for an older Core API.
- Document preparation ordering, shader variants, precision and material ownership in English, Korean and Japanese.

### Fixes

- Freeze decoder input in a reusable linear Float32 snapshot so header, calibration and payload passes use the same image. Discard pending readbacks after disable, release owned snapshots and preserve the existing wire format.
- Preserve RPC queue accounting under reentrant sends and reject events that cannot fit the configured payload. Retransmission remains a finite attempt budget, not guaranteed delivery.
- Pace FFmpeg output independently of incoming updates while retaining the latest frame.
- Ignore TransSync reception before value decoding when the target selects None, and clear pending Rigidbody velocities when physics reception is disabled.
- Invalidate stale BlendShape interpolation targets after selection, renderer, mesh or receive-state changes, and apply Animator layers only when selected by the receiver.
- Correct Animator drift handling for loop boundaries and non-looping states; read negative state hashes without Udon numeric conversion failures.
- Preserve integer-sized pixel blocks during output expansion and clear right/bottom remainder pixels for dimensions not divisible by block size.
- Commit avatar root delta and keepalive state only after successful output through the opt-in `TransSync.SentEvent` callback; guard encoder reentry during callbacks.

Release candidates must be validated and Core published before the dependent codec releases. This heading does not indicate that the version is already available from VPM or GitHub Releases.

## 0.3.0-beta.1

### Added

- Add per-field TransSync send scheduling in native Unity and Udon: descending Priority, content-based SendOnChange and MinSendInterval measured from successful output.
- Add configurable unchanged-value refresh (one second by default), equal-priority rotation and capacity deferral with diagnostics. Reserve queued RPC payload before automatic variables and retain unsent state for retry. The wire format is unchanged.

### Timeline

- Apply the first received Timeline position after preparing playback, independently of the drift threshold. Prepare paused graphs before evaluating and avoid rebuilding stopped graphs for repeated packets.
- Implement Timeline Continuous receive correction, including shortest-path correction across loop boundaries. Clear pending corrections when reception is disabled, the component is disabled, or the Director changes.
- Reject malformed Timeline packets, unknown states and invalid times before changing playback. Clear outgoing Timeline data when its source is unavailable.
- Make Timeline Play and Resume work after Stop without restarting an already playing Director. Add Seek(float) with playback-state preservation and local duration bounds.
- Add native Timeline regression tests, real Udon VM receive tests and animated Timeline texture loopback coverage in the Windows Mono Player validation.

### Streaming

- Reject FFmpeg source/output dimension mismatches before process startup, and stop publishing if the source size changes. Validate every RGBA32 readback before row flipping or writing; do not resize TSMP pixels implicitly.
- Isolate FFmpeg processes, buffers, output diagnostics and GPU callbacks per publishing session so stopped sessions cannot submit into a restarted publisher.
- Replace writer interruption with cooperative shutdown and process termination for blocked pipes. Dispose session resources after the writer finishes, detect unexpected process exits and restore background execution on failure or shutdown.

## 0.2.0

- Promote 0.2.0 out of beta with the SDK-optional Unity support and shared Controller workflow introduced in 0.2.0-beta.1.
- Improve humanoid Continuous interpolation by converting received world rotations to local targets, handling skipped ancestors and applying root rotation only once (PR #1 by AYANO-TFT).
- Reserve existing Network IDs before assigning IDs to new objects or resolving duplicates.
- Refresh binding, humanoid rig, Animator parameter and codec configuration caches when their contents change, including same-length replacements. Check decoder binding configuration once per network frame.
- Preserve configured codec instances when Setup adds, removes or reorders codec sources, or changes the instance root.
- Read actual Timeline state in native Unity. Add Play, Pause, Resume and Stop controls for explicit Udon playback-state tracking instead of inferring state from time deltas.
- Keep the Decoder payload buffer at its actual size between readbacks instead of reallocating it to 4096 bytes before every header.
- Give each decoder binding its own received array buffer so updating one field cannot overwrite another field or recipient. Reuse buffers for same-length updates within each binding.
- Prevent duplicate automatic encoding in the SDK-free Editor: the native Encoder drives itself, while Setup delegates editor encoding only when UdonSharp is present. Manual encoding and automatic setup preparation are unchanged.
- Restore discrete blendshape values on receipt when animation or another script has changed the Renderer since the previous packet.
- Use the same bounded Animator selection for packet sizing and writing. Limit parameters and layers to 255 entries, reject unrepresentable layer indices, and enforce selection limits in the Inspector.
- Consume native Encoder RPC repeats only after successful frame output, preserving pending events when payload construction, capacity checks or codec writing fail.
- Include Stream ID in the Decoder RPC deduplication key so independent streams can reuse event IDs without losing calls.

## 0.2.0-beta.1

- Support installation, native component bindings, encoding, decoding, and Windows Mono Player builds without VRCSDK/UdonSharp.
- Keep SDK-dependent player capture, proxy APIs, and editor tooling conditional while preserving the existing VRChat/UdonSharp paths.
- Use one shared Controller prefab with automatic component, codec, binding, and per-controller resource preparation.
- Preserve existing Controller references through the Legacy prefab with its original GUID.
- Add repeatable native GPU loopback, Player, UdonSharp, SDK world build, and shared workflow validation.
- Update English, Korean, and Japanese installation and setup documentation.
- Use Luma4 0.0.3-beta.3 or newer for the shared SDK-neutral codec template.
- IL2CPP, managed stripping, and an uploaded VRChat client session remain outside the verified configuration.

## 0.1.0

- Promoted the package version out of beta.
- Added the complete TSMP sample assets under `Samples`.

## 0.0.3-beta.4

- Added inspector error boxes for TransSync variable ID collisions.
- Kept detailed TransSync collision logs in the Unity console.
- Fixed UTF-8 encoding for non-ASCII BMP characters in synced strings.

## 0.0.3-beta.3

- Fixed UTF-8 string encoding for three-byte BMP characters, including variation selectors used by emoji sequences.

## 0.0.3-beta.2

- Merged the latest main branch updates into the release branch.
- Added the TransSync values debug canvas sample update.
- Updated localized README and documentation homepage content.

## 0.0.3-beta.1

- Beta release metadata for VPM distribution.
- Includes core runtime, encoder, decoder, setup tooling, shared network behaviours, and codec authoring APIs.

## 0.0.2

- Fixed intermittent editor compile failures during UdonSharp define transitions.

## 0.0.1

- Initial beta package release.

## 1.0.0

- Initial package split with encoder and decoder runtime included in core.
