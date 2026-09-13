# Issue #22 validation results

Date: 2026-09-13. Branch: `fix/decoder-frame-snapshot`, based on Core `f77f957`.

## Change

Previously, the decoder saved a Texture reference but read the header and payload at different times. A valid header CRC did not prevent the payload from coming from a later image. The unchanged baseline reproduced this as a header/payload generation mismatch in the first Luma4 case.

`DecoderSnapshotRuntime` now owns one same-size, linear ARGBFloat render target. `TSMPDecoder.DecodeNow()` copies the input before requesting the header. Header sampling, codec preparation/calibration and payload sampling all use this snapshot. There is no live-source fallback or codec-specific snapshot policy. Float32 avoids introducing an additional 8-bit or half-float quantization step for third-party codecs.

Disable releases the snapshot and marks any pending readback for discard. A rapid re-enable waits for that callback to drain before allowing another capture. Destroy also releases the resource. Public setup fields, codec IDs, wire bytes and frame-application algorithms are unchanged. No additional Inspector control is required.

## Environment

| Item | Version / configuration |
| --- | --- |
| Unity | 2022.3.22f1, Windows |
| GPU / API | NVIDIA GeForce RTX 4090, Direct3D 11 |
| Core baseline | `ce55d15`, archived package; later parent `f77f957` only adds validation artifacts |
| Core under test | Working package based on `f77f957` plus this commit's runtime changes |
| Core manifest | 0.3.0-beta.2 candidate, not released by this work |
| Luma4 | `f7f3d82`, 0.0.4-beta.1 candidate |
| RGB16 | `eb69fe5`, 0.0.3-beta.3 candidate |
| RGB20 | `3373c73`, 0.0.3-beta.4 candidate |
| Color256 | `97b6e00`, 0.0.3-beta.3 candidate |
| SDK / UdonSharp | VRChat Base + Worlds 3.10.4-beta.2, bundled UdonSharp |
| Player | Windows x64, Development, Mono, managed stripping disabled, Linear color space |

SDK-free tests referenced the actual working packages. SDK tests used isolated embedded copies to avoid modifying tracked generated assets. All 137 C#, shader, cginc, asmdef and JSON files across those copies matched the working packages by SHA-256 after testing.

## Results

| Test | Result | Evidence |
| --- | --- | --- |
| Unchanged baseline | Expected failure: valid A header with non-A payload | [before.txt](Evidence/before.txt) |
| SDK-free Gamma Play Mode | Passed 216 cases, lifecycle, native value dispatch and Float32 precision | [gamma.txt](Evidence/gamma.txt) |
| SDK-free Linear Play Mode | Passed 216 cases, lifecycle, CRC rejection, native value dispatch and precision | [linear.txt](Evidence/linear.txt) |
| Windows Player build | Succeeded; 0 errors, 16 shader warnings | [player-build.txt](Evidence/player-build.txt) |
| Windows Player execution | Passed 216 cases and additional correctness checks, using actual D3D11 readback | [player.txt](Evidence/player.txt) |
| UdonSharp full client-program compile | Passed | [udon.txt](Evidence/udon.txt) |
| Compiled Udon VM execution | Passed 16 frame cases and 8 actual disable/cancellation cases | [udon.txt](Evidence/udon.txt) |
| Website | English/Korean/Japanese production build and TypeScript check passed | `npm run build`; `npm run typecheck` in `Website~` |
| Actual VRChat client / SDK world build | Not run | Editor VM execution is not a substitute for this result |
| Android, other GPUs/APIs, IL2CPP | Not run | No compatibility or performance claim for these configurations |

The Gamma run predates the explicit corrupted-header test, which passed in the final Linear Editor and Player runs. The shader warnings were emitted by the existing codec shaders; no shader source changed in this fix. Player GPU timestamp counters were unavailable, so Player results are correctness evidence, not timing evidence.

## Copy and memory cost

Linear Editor diagnostic-run measurements, fifteen positive GPU timestamp samples per size:

| Size | Snapshot storage | Median copy time |
| --- | --- | --- |
| 640 x 360 | 3,686,400 bytes (3.52 MiB) | 10.400 microseconds |
| 1920 x 1080 | 33,177,600 bytes (31.64 MiB) | 78.624 microseconds |
| 3840 x 2160 | 132,710,400 bytes (126.56 MiB) | 80.576 microseconds |

These are synthetic, repeated-copy measurements on one high-end GPU, not per-frame budgets for production or VRChat. The earlier Gamma run measured 271.360 microseconds at 4K and did not produce valid counters at smaller sizes. The final material-isolated rerun measured 79.616 microseconds at 1080p and 80.384 microseconds at 4K, but its 640x360 counters were unavailable. This variation means the table must not be extrapolated to other content or GPUs. End-to-end latency and VRChat frame timing were not measured. The diagnostic timing run is preserved in [timing.txt](Evidence/timing.txt).

The copy happens before header validation, including attempts subsequently rejected as corrupt or duplicate. One snapshot is reused, not retained per frame. This removes mixed input generations but does not fix an upstream producer that already outputs a torn/corrupt image, or separate downstream atomic-application and frame-ordering issues.

## Validation iterations and limits

- An initial test asserted duplicate suppression after readback-only mode, which intentionally does not record an applied frame. The test was corrected to apply a normal frame before testing duplicate suppression; production duplicate logic was not changed.
- Two initial Linear Editor runs exceeded a 15-second test wait. The diagnostic rerun used a 60-second wait and per-case logging and passed. No runtime timeout logic changed, and the precise cause of those initial delays was not established.
- The initial Udon run included native-only codec APIs in a test file visible to the Udon compiler. Guarding that test helper with `!COMPILER_UDONSHARP` fixed the harness compilation; the final full client-program compile and VM tests passed.
- The initial native harness modified shared prefab materials, which Unity later saved during a build. The harness now clones and releases those materials. Only the test-generated asset changes were restored, and the final rerun left all codec repositories unchanged.
- A hidden non-batch Player did not complete the first header callback within 60 seconds. The same executable passed with graphics-enabled `-batchmode`; a fresh build and execution through the final wrapper also passed. Minimized/hidden normal-window operation is not certified by this run.
- Float32 render-target support/allocation is required. Failure is logged and the decode attempt is rejected. Unsupported-device and allocation-exhaustion behavior were not exercised on this GPU.

## Local artifacts

Base directory: `F:/Unity/TSMP/Validation-Results/issue22-20260913/`.

- Isolated projects: `Before`, `After`, `VRC`.
- Baseline: `results/before/Play-editor.log` and `Play.txt`.
- Gamma: `results/after-final/Play-editor.log` and `Play.txt`.
- Linear: `results/isolated-playlinear/PlayLinear-editor.log` and `PlayLinear.txt`.
- Diagnostic copy timing: `results/linear-diagnostic/PlayLinear-editor.log` and `PlayLinear.txt`.
- Udon: `results/isolated-udon/Udon-editor.log` and `Udon.txt`.
- Player build/import: `results/isolated-player/Player-editor.log`.
- Player execution: `results/isolated-player/Player.log` and `Player.txt`.
- Executable: `results/isolated-player/Player/Snapshot.exe`.
- Build report summary: `results/isolated-player/Player/Snapshot.build-report.txt`.

Earlier failed attempts remain under `results/after`, `results/linear`, `results/linear-retry`, `results/udon` and `results/player`. Compact final results and the baseline failure are committed under `Evidence`; large logs, generated projects and binaries are not.
