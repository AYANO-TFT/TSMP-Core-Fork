# Resource optimizations implemented

This records five independently branched changes following the feasibility report. Each branch is merged to main only after its validation. No package versions were bumped, pushed or released by this work.

## Changes

| Step | Branch | Implementation |
| --- | --- | --- |
| 1 | `optimize/udon-bulk-copy` | Guarded `Array.Copy` for same-type image, payload and receiver copies. Receiver buffers remain independent. Core `ec259d7`. |
| 2 | `optimize/raw-byte-readback` | Read GPU bytes into independent slot-owned arrays; validate request byte counts before bulk header/payload extraction. Core `10a12fd`. |
| 3 | `optimize/native-raster-reuse` | Reuse encoder-owned native raster arrays through optional `TryWriteFrameBuffered`; default delegates to existing plugin API. Core `f21c39f`, Luma4 `72d05a6`. |
| 4 | `optimize/snapshot-storage` | Match known 8-bit/half-float storage and color interpretation. Preserve Float32 for unknown/high-precision sources. Core `c998e19`. |
| 5 | `optimize/gpu-luma4-encoding` | Upload raw header/payload bytes, render a small Luma4 symbol texture, then expand into output. Reuse private upload/symbol buffers, prepare the material automatically, retain CPU fallback and explicit native codec opt-in. |

All five preserve protocol bytes, codec IDs, frame ordering, CRC checks, two-slot ownership, prediction validation and RPC repeat/deduplication behavior. No frame is skipped because only its payload length stayed the same.

## Environment

- Unity 2022.3.22f1, Windows 11, i9-13900K, RTX 4090, D3D11.
- Native Windows x64 Development Player: Mono, managed stripping disabled, graphics enabled.
- Worlds SDK 3.10.4-beta.2 and bundled UdonSharp: complete client-target compilation, execution in the actual SDK Editor VM, real GPU rendering/readback.
- Native pixel tests in both Gamma and Linear. Full decoder regression includes all four installed codecs; public user documentation describes only Luma4.
- Runtime profiles use one always-sent byte-array field, Luma4, block size 8, sample size 1. A 4096-byte value occupies 4121 payload bytes; a 32-byte value occupies 57 bytes.
- Profiles warm for three seconds, measure twelve seconds, then continue briefly and drain. These are explicit submission tests, not live OBS/MediaMTX/VRChat measurements.

## CPU and allocation results

Compare against [the original profile](../ContinuousFrames/RESOURCE-PROFILE.md), not against summed nested method timings. Times are means; submission and completion are separate events.

| Workload | Original | After these changes |
| --- | ---: | ---: |
| Udon 720p / 4 KiB / 60 Hz, encoder | 11.27 ms | 0.457 ms |
| Same, decoder submission | 0.384 ms | 0.370 ms |
| Same, readback callback | 3.52 ms | 0.421 ms |
| Sum of those three means | 15.17 ms | 1.248 ms |
| Native 720p / 4 KiB, publish | 3.33 ms | 0.095 ms |
| Native 4K / 32 B, publish | 20.72 ms | 0.141 ms |
| Native 720p, median frame GC allocation | 3,686,964 B | 612 B |

The Udon encoder p95 was 0.546 ms and callback p95 0.495 ms in the final repeated profile. The 1.248 ms sum is not a measured p95 frame time and excludes avatar capture, IK, interpolation, streaming and world rendering. A receiver-only process does not pay encoder CPU time.

The native allocation counter covers the whole validation Player. The first measured frame contains a harness-related multi-megabyte spike and three collections occurred during the 720p case. Scoped allocation counters remain unavailable on this Mono runtime. This is removal of image-array allocation churn, not a zero-allocation or leak-free claim.

## Small payloads and GPU tradeoff

The production Udon raster microbenchmark includes byte upload and CPU submission, but not GPU completion:

| Output / payload bytes | Previous CPU raster | GPU writer CPU time |
| --- | ---: | ---: |
| 360p / 0 | 0.081 ms | 0.066 ms |
| 360p / 32 | 0.158 ms | 0.066 ms |
| 720p / 256 | 0.422 ms | 0.063 ms |
| 720p / 4096 | 4.629 ms | 0.072 ms |
| 4K / 32 | 0.200 ms | 0.064 ms |
| 4K / 63360 | 63.481 ms | 0.117 ms |

Native full-raster CPU work also improved at every measured size, including empty payloads. Consequently there is no speculative payload-size threshold: eligible small frames use the GPU path too. The existing CPU path remains available with `useGpuLuma4=false` or `useBlockSymbolTexture=false`, when required resources are absent, when native shader support is unavailable, or for ineligible layouts.

This does add a GPU conversion pass. Dedicated Editor timestamp tests used 32 draws per sample, 15 warm and 25 measured frames with explicit camera rendering. At 720p the Gamma conversion-plus-expansion median was about 16.5 us versus 8.3 us for block expansion alone; at 4K about 93.8 us versus 81.0 us. Clock/recorder variation was visible, especially in repeated small cases. Upload transfer, readback and world rendering are excluded. Final Player GPU timestamps were unavailable, not zero. The result is substantially less CPU work at a small measured GPU draw cost on this GPU, not universally lower GPU time.

The GPU path requires whole blocks, at least 38 blocks across, a payload start row no earlier than row 5, and enough room for header/payload/end marker. Native plugins opt in with `SupportsGpuLuma4Encoding=false` by default. Only standard Luma4 raster semantics may opt in. Udon already selects built-in Luma4 rasterization by symbol mode; all other plugin writers are unchanged.

## Memory

Two recognized 8-bit input snapshots occupy 7.03 MiB at 720p instead of 28.13 MiB, and 63.28 MiB at 4K instead of 253.13 MiB. This is 75% less snapshot texel storage, not a 75% reduction in total process memory.

The 4K GPU encoder retained a 480x270 RGBA8 symbol target (518,400 bytes), a 1,024-byte upload texture plus its CPU copy, and a 1,024-byte managed upload array for the small-payload case. The full-size CPU staging image and raster arrays were not allocated on that path. The final output texture is still required. Buffers grow as needed and are reused; shrinkage does not require clearing stale uploaded bytes because shader reads are bounded by the current payload length and every output symbol is redrawn.

Native format detection uses graphics format. Udon does not expose `Texture.graphicsFormat` or `QualitySettings.activeColorSpace` in the tested SDK. It uses RenderTexture format/sRGB instead and keeps Float32 for other source types. A format/color interpretation change recreates idle storage; pending slots keep their own snapshots. No required Inspector option or context-menu operation was added.

## Correctness evidence

- Same-type copies: null/empty cases, invalid ranges before mutation, overlap, unequal image lengths, exact-size receiver caches and independent buffer ownership.
- Raw readback: header offsets, padding, multi-row data, all codec switches, growth/shrink, CRC failure and pending callback cancellation.
- Native raster reuse: 24 full-image comparisons, independent buffers and dimension changes.
- Snapshot selection: 24 source/filter/format combinations in each color space, Texture2D and RenderTexture, all byte levels and negative/HDR values. Automatic snapshots matched the Float32 sampled baseline within 1e-7. Manual lower-precision candidates remain in the evidence as counterexamples.
- Full snapshot suite: 216 changing-source codec cases, sample sizes 1/4, both orientations, high precision, lifetime and frame-window checks.
- GPU encoder: 40 full-pixel native comparisons in each color space, including empty/1/3/32/256/large/capacity payloads, 81-block odd width, resize and shrink. Udon CPU/GPU full-image comparisons passed for 360p/720p/4K and empty/small/large/capacity/shrink cases.
- Native Player and client-target Udon integration: CPU/GPU switches, automatic material serialization, disable/re-enable resource cleanup, codec switches, source overwrite, CRC rejection, concurrent decoder isolation, ordered two-slot completion, cancelled slots and exactly-once repeated RPC tests.
- The native and Udon profiles received all 361/361 published frames at 30 Hz and all 721/721 at 60 Hz for the measured small/large cases. 4K small-payload publication sustained approximately 30 Hz, whereas the original Udon profile only published at 23.53 Hz. This is finite-test evidence, not a delivery guarantee.

## Reproduction and artifacts

Compact evidence is under `Implemented/01-bulk` through `Implemented/05-gpu`. Full logs and profiling files are under `F:/Unity/TSMP/Validation-Results/resource-optimizations/` on the measurement machine.

```powershell
./Validation~/OptimizationFeasibility/Run-Validation.ps1 -ProjectPath F:/Unity/TSMP/Validation-Codecs-VRC -ResultsDirectory F:/Unity/TSMP/Validation-Results/resource-optimizations/05-gpu/udon -Mode Udon
./Validation~/OptimizationFeasibility/Run-Validation.ps1 -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK -ResultsDirectory F:/Unity/TSMP/Validation-Results/resource-optimizations/05-gpu/linear-draw -Mode GpuLinear
./Validation~/ContinuousFrames/Run-Validation.ps1 -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK -ResultsDirectory F:/Unity/TSMP/Validation-Results/resource-optimizations/05-gpu/final-player -Mode Player -Filter 'profile-large60,prediction-regression,overlap-regression'
./Validation~/ContinuousFrames/Run-Validation.ps1 -ProjectPath F:/Unity/TSMP/Validation-Codecs-VRC -ResultsDirectory F:/Unity/TSMP/Validation-Results/resource-optimizations/05-gpu/udon-final -Mode Udon -Filter 'profile-large60,profile-4k-small30,prediction-regression,overlap-regression'
```

Run Unity measurements sequentially. `GpuGamma` and `GpuLinear` change only the isolated project's color space. `TSMP_GPU_SMOKE=1` skips timing loops but retains every pixel comparison. BuildReport and the executable are under `05-gpu/final-player/Player/`; Player.log is in its parent directory. The broader five-case Player measurement is in `05-gpu/native-player/`. Generated Udon program changes are backed up externally and excluded from source commits.

## Remaining validation and release requirements

No live VRChat client, Quest/mobile GPU, Metal/Vulkan, OBS/MediaMTX stream, IL2CPP/stripping, real-avatar scene or long-duration soak was tested. SDK Editor VM execution cannot certify client serialization and platform-specific behavior. Existing unrelated decoder shader warnings remain and are recorded in build logs. Do not convert unavailable GPU/allocation measurements into zero-cost claims.

The newly changed Luma4 source requires the matching unreleased Core APIs. Before publishing, set matching Core/Luma4 package dependency versions and regenerate release artifacts. Old third-party native writers remain valid through the fallback. The published image is `TSMPEncoder.output`; hidden `outputTexture` is CPU staging and is not updated on the GPU path.
