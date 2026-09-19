# Decoder delivery at a 30-FPS Unity loop

## Scope

This is a measurement-only change. The production Core and codec sources are unchanged from the two-slot implementation. Tests run the real Encoder, codec shaders, Decoder and receiver callback, with prediction and combined byte output enabled. Two slots are compared against the advanced single-slot opt-out.

Environment: Unity 2022.3.22f1, Windows x64, D3D11, Gamma, RTX 4090, i9-13900K. Native tests use a Development Mono Player with stripping disabled and no SDK. Udon tests use Worlds 3.10.4-beta.2, client-target UdonSharp compilation and the actual SDK VM with real GPU callbacks. This is a frame-rate cap on high-end hardware, not a GPU-bound 30-FPS world or a live VRChat client test.

The tested source is Core commit `94eaa45`, which only adds harness cases on top of runtime commit `2911bcc`. Codec source commits are Luma4 `30f422c`, RGB16 `10e7232`, RGB20 `d3771cd`, Color256 `5772d0b`. Delivery measurements in this report use Luma4 with sample size 1. Package versions remain Core 0.3.0-beta.2, Luma4 0.0.4-beta.1, RGB16/Color256 0.0.3-beta.3 and RGB20 0.0.3-beta.4, with the unreleased changes in those commits.

## Method

- VSync is disabled and `Application.targetFrameRate` is 30. Measured `loopHz` verifies the achieved rate rather than assuming the cap was met.
- A small value is 32 bytes, or 57 payload bytes including network framing, in a 640x360 image. The larger value is 4096 bytes, or 4121 payload bytes, in a 1280x720 image.
- `small-30-at-30` and `small-60-at-30` retain the existing wall-clock send scheduler. The latter intentionally asks for 60 publications in a process that can update only about 30 times per second.
- `hd-large-every-frame-at-30` and `sustained-every-frame-at-30` publish once per actual Unity update to avoid under-driving the receiver when send deadlines and frame timing drift. There is no catch-up burst. The CSV identifies this as `publicationMode=every-update`; deadline-miss counting is not applicable to this mode.
- Each case has a preflight frame, two seconds of warmup, twelve seconds of measurement (sixty for sustained), one second of continuing unmeasured publication and a final pending-request drain. Received counts include measured IDs applied during that tail; in-window apply Hz is reported separately.
- Actual application is recorded by the receiver's `OnTSMPVariableReceived` callback. IDs, complementary check bytes and application order are checked. Frame loss is missing **actually published** IDs, not missed sender deadlines or skipped duplicate capture attempts.
- Runs are serial. The native one-slot control uses the identical executable as the native two-slot run. No production source is altered between settings.

## Native results

| Case | One-slot received/published | Two-slot received/published | One / two-slot loss | One / two-slot p50 latency | One / two-slot p95 latency |
| --- | --- | --- | --- | --- | --- |
| 30-Hz send target, small, 12 s | 305/360 | 360/360 | 15.28% / 0% | 33.30 / 66.63 ms | 66.64 / 66.76 ms |
| 60-Hz send target, small, 12 s | 317/359 | 360/360 | 11.70% / 0% | 33.30 / 66.64 ms | 66.65 / 66.80 ms |
| Every update, large, 12 s | 293/360 | 360/360 | 18.61% / 0% | 33.30 / 66.62 ms | 66.66 / 66.82 ms |
| Every update, small, 60 s | 1579/1800 | 1799/1799 | 12.28% / 0% | 33.30 / 66.63 ms | 66.64 / 66.78 ms |

The two-slot sustained run achieved 29.9833 Hz with no missing published IDs and no capacity-blocked capture ticks. Its first/last-third mean latency was 66.41/66.14 ms, with maximum latency 79.71 ms. No growing backlog was observed. The 1799 count is the number of actual updates/publications in the sixty-second window, not an unreceived 1800th packet. In-window apply Hz was 29.95; the last two measured IDs arrived in the observation tail.

## Udon results

| Case | One-slot received/published | Two-slot received/published | One / two-slot loss | One / two-slot p50 latency | One / two-slot p95 latency |
| --- | --- | --- | --- | --- | --- |
| 30-Hz send target, small, 12 s | 356/360 | 360/360 | 1.11% / 0% | 33.27 / 66.59 ms | 33.51 / 66.85 ms |
| 60-Hz send target, small, 12 s | 357/360 | 360/360 | 0.83% / 0% | 33.26 / 66.59 ms | 33.51 / 66.84 ms |
| Every update, large, 12 s | 351/360 | 360/360 | 2.50% / 0% | 33.26 / 66.59 ms | 35.00 / 68.15 ms |
| Every update, small, 60 s | 1758/1800 | 1799/1799 | 2.33% / 0% | 33.26 / 66.59 ms | 33.53 / 66.86 ms |

The two-slot sustained run achieved 29.9833 Hz. No capacity-blocked capture ticks, corrupt values, out-of-order applications or decoder error observations occurred. First/last-third mean latency was 66.48/66.37 ms, maximum 67.30 ms. In-window apply Hz was 29.9667; the last measured ID arrived during the observation tail. The one-slot control missed 42 of 1800 published IDs. Client-target UdonSharp compilation completed and automatic packing-material preparation passed in both runs.

## Interpreting 60-FPS input

The 60-Hz send-target case did not produce an independent 60-FPS stream. The two-slot run actually published 360 frames over twelve seconds, while skipping 360 sender deadlines **before encoding**. Receiving those 360 frames is not proof that a 30-FPS receiver can preserve 60 distinct images each second.

The production automatic decoder starts at most one new capture per Unity `Update`. With a sustained 30-Hz update loop, it therefore samples at most about 30 source images per second. Two slots overlap processing; they do not retain every intermediate image overwritten in the input texture. If an independent source actually publishes 60 distinct images per second and playback continues in real time, this path cannot capture all of them (at most approximately half over a long steady interval). This is a code-derived limit, not an external-source measurement performed here.

## Latency and limits

Two-slot operation can preserve more captured updates while increasing median application latency. At this cap, the native median was about two Unity intervals (66.6 ms), versus about one interval in one-slot mode. Zero loss here is not evidence of unlimited headroom: a longer GPU/readback stall, more expensive receiver handlers or extra world load can fill both slots and reject new captures. Bounded slot count prevents an unbounded frame queue, not arbitrary wall-clock delay.

These local tests bypass OBS, Spout, streaming servers, compression and network loss. They use one simple bound byte-array value, not multiple animated avatars or a populated world. No external 60-FPS source, lower-end GPU, IL2CPP, Quest, Linear color space or other graphics API was measured.

## Reproduction and artifacts

Set `TSMP_AUTOMATIC_SCHEDULING=1`, `TSMP_DISABLE_COMBINED_OUTPUT=0`, `TSMP_DISABLE_PREDICTION=0`. Set `TSMP_SINGLE_SLOT=0` for the current default and `1` for control.

```powershell
./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/decoder-30fps/native-two-slots `
  -Mode Player `
  -Filter 'small-30-at-30,small-60-at-30,hd-large-every-frame-at-30,sustained-every-frame-at-30'

./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-VRC `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/decoder-30fps/udon-two-slots `
  -Mode Udon `
  -Filter 'small-30-at-30,small-60-at-30,hd-large-every-frame-at-30,sustained-every-frame-at-30'
```

Full results are under `F:/Unity/TSMP/Validation-Results/decoder-30fps/`. The native executable is `native-two-slots/Player/ContinuousFrames.exe`; its adjacent `.build-report.txt` records a successful build with zero errors and 16 shader warnings. Compact evidence is retained in `Evidence/ThirtyFps`.

Native Player logs also contain `Releasing render texture that is set to be RenderTexture.active!` warnings during decoder shutdown between cases. These did not produce observed corruption or receive errors, but the cleanup warning is recorded rather than claiming warning-free execution. This measurement task does not change the release path.
