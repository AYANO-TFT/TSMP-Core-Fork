# Bounded two-slot decoder overlap

## Scope and design

`overlapReadbacks` defaults to true and permits two retained source images. Disabling it limits new admissions to one without cancelling accepted work. A full decoder rejects new captures; there is no spill queue or third image. This change is independent of combined byte output and does not change the wire datagram, codec IDs, frame ordering rules, RPC repeat budget or RPC deduplication.

Each slot owns its snapshot, header/combined targets, byte buffers, captured metadata and prediction expectations. Slot zero uses the assigned payload byte texture; slot one owns a separate byte target. A callback stores bytes in its own slot. The consumer drains in capture order, waiting for an older header/payload fallback before applying a younger completed frame. Failed or cancelled heads retire without application. Reentrant decode calls during dispatch are rejected. Disabling marks both slots cancelled; pending request storage is retained until completion and cannot be reused early.

The SDK readback API returns the same request wrapper that arrives in its callback. Udon routes completion by that object's identity through `Equals`, verified in compiled Udon execution. Native Unity uses two distinct callbacks. Neither path infers request identity from FIFO callback delivery, dimensions or texture size.

The immutable source remains ARGBFloat: a 1920x1080 image costs about 31.64 MiB per allocated slot, about 63.28 MiB for two, plus byte textures/managed buffers. Allocation is reused, but every accepted capture still copies the input. More admitted frames also mean more GPU work and CPU/Udon application work. Disabling overlap does not immediately release previously allocated slot storage.

## Environment and method

Unity 2022.3.22f1, Windows x64, D3D11, RTX 4090, i9-13900K, Gamma. Native tests are Development Mono Players with stripping disabled and no SDK. SDK tests compile the actual programs for the client target and run them through the Worlds 3.10.4-beta.2 SDK VM with real GPU requests/callbacks. No `-nographics` runs are used.

Core is unreleased source on top of 0.3.0-beta.2; the runtime change is commit `2911bcc`. Initial result files record parent `d497c0f` because the tested source was then uncommitted. The native enabled/control pair uses the identical executable. The codec repositories include the preceding combined-output commits: Luma4 `30f422c`, RGB16 `10e7232`, RGB20 `d3771cd`, Color256 `5772d0b`. Their package versions remain 0.0.4-beta.1, 0.0.3-beta.3, 0.0.3-beta.4 and 0.0.3-beta.3 respectively. No package version or release tag is changed.

Runs are serial, use automatic decoder updates, and keep prediction and combined output enabled unless explicitly testing fallback modes. Sender deadlines are wall-clock paced with no catch-up burst. Received counts are unique published IDs observed after the measurement tail/drain, not GPU submissions or duplicate images. Target 60 Hz is not evidence that 60 frames were actually published.

## Paired measurements

| Runtime / case | Single-slot received/published | Two-slot received/published | Single / two-slot p50 latency | Single / two-slot p95 latency |
| --- | --- | --- | --- | --- |
| Native 60/60, 57-byte payload, 12 s | 635/713 | 715/715 | 16.64 / 33.30 ms | 33.30 / 33.38 ms |
| Native 60/120, 57-byte payload, 12 s | 720/720 | 720/720 | 8.34 / 16.64 ms | 16.65 / 16.70 ms |
| Native 60/120, 4121-byte payload, 12 s | 720/720 | 720/720 | 8.35 / 16.64 ms | 16.68 / 16.70 ms |
| Native 60/60, 57-byte payload, 60 s | 3213/3600 | 3599/3599 | 16.64 / 33.30 ms | 33.30 / 33.40 ms |
| Udon 60/60, 57-byte payload, 12 s | 699/708 | 613/613 | 16.61 / 33.23 ms | 16.86 / 33.53 ms |
| Udon 60/120, 57-byte payload, 12 s | 720/720 | 720/720 | 8.42 / 16.61 ms | 8.55 / 16.80 ms |
| Udon 60/120, 4121-byte payload, 12 s | 499/720 | 720/720 | 27.50 / 32.34 ms | 28.81 / 34.03 ms |
| Udon 60/60, 57-byte payload, 60 s | 3257/3297 | 2766/2766 | 16.61 / 33.27 ms | 16.87 / 33.51 ms |

The native sustained case reduced missing published frames from 10.75% to zero; first/last-third latency means were 33.17/33.21 ms with overlap, so no growing backlog was observed. The Udon large case reduced missing frames from 30.69% to zero, while achieved render-loop rate fell from 78.08 to 66.33 Hz as more data was decoded/applied. This is not free CPU throughput.

The Udon 60/60 cases did **not** sustain the target publication rate: the sustained sender published 54.95 Hz in control and 46.10 Hz with overlap. Deadline skips are recorded separately in the CSV. Zero receiver loss in that row is not a 60-Hz delivery result, and overall applied Hz was lower despite the reduced receiver miss fraction. Scheduling and measurement overhead affect these in-process sender/receiver tests. Both paths published all 720 target frames in the 120-Hz-loop twelve-second cases.

An additional Udon run used a 120-Hz loop for a full minute of 60-Hz publication: 3600/3600 frames received, no sender misses, p50 16.61 ms, p95 16.74 ms, p99 16.90 ms and maximum 17.10 ms. First/last-third means were 16.47/16.57 ms. This demonstrates sustained delivery in that local setup, not in a live client or on a lower-end GPU. See `udon-sustained` evidence.

The matching additional native run (`native-confirm`) also received 3600/3600 with no sender misses, p50 16.64 ms, p95 16.70 ms and maximum 20.27 ms. First/last-third means were 16.54/16.58 ms. Both counts include the normal observation tail after the sixty-second measurement window; in-window application rate was 59.98 Hz.

Overlap is therefore not a latency reduction. It can trade about one additional update of median latency for fewer missed capture opportunities, and can worsen latency where one slot already delivers everything. Select the advanced opt-out when low latency/memory matters more than the measured loss reduction. Single paired runs on one high-end GPU do not establish a universal improvement or safe headroom in a populated world.

## Regression coverage

Native Player and compiled Udon tests cover real simultaneous captures, third-capture rejection, changing codec/sample/payload length, same-snapshot fallback after source overwrite, cancellation of two pending slots and recovery, plus repeated RPC copies applied exactly once. Prediction regressions additionally validate current-header CRC rejection and concurrent independent decoder instances.

The readiness-order test first waits for real GPU completions with the consumer paused, then holds the older slot pending using test-only private-state access. It verifies a younger ready result cannot overtake it. Separate controlled cases discard the head or inject an error after GPU completion, verifying that the successor can proceed. This is a deterministic state-machine test, not a claim that hardware callbacks naturally arrived out of order or that a real GPU device failure occurred.

The first expanded native run (`native-final`) was stopped by the harness's fail-on-error listener when the intentionally injected error was logged. The injected-failure case now temporarily disables the decoder's diagnostic logging, restores it after draining, and still checks the application count and successor bytes. That retained failure is a test-harness correction, not an unexplained production failure. The initial combined-output cold-start timeout is separately retained in [that experiment's report](COMBINED-OUTPUT.md).

## Reproduction and artifacts

Use the isolated projects listed in [the harness instructions](README.md); their file dependencies reference the actual modified Core and codec repositories. Set `TSMP_AUTOMATIC_SCHEDULING=1`, `TSMP_DISABLE_COMBINED_OUTPUT=0`, and leave `TSMP_DISABLE_PREDICTION` unset. Compare `TSMP_SINGLE_SLOT=1` against `0` with the same scheduling/filter settings.

```powershell
./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/two-slots/native-enabled `
  -Mode Player `
  -Filter 'prediction-regression,overlap-regression,small-60-at-60,small-60-at-120,hd-large,sustained-60'

./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-VRC `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/two-slots/udon-enabled `
  -Mode Udon `
  -Filter 'prediction-regression,overlap-regression,small-60-at-60,small-60-at-120,hd-large,sustained-60'
```

Full local logs, traces, executables and BuildReports are under `F:/Unity/TSMP/Validation-Results/two-slots`. Native artifacts include `native-enabled/Player/ContinuousFrames.exe`, its adjacent `.build-report.txt`, and `Player.log`. Compact evidence is retained in `Evidence/TwoSlots`. The initial Player build succeeded with zero errors and 16 shader warnings (potentially uninitialized sampling/classification values and integer-division cost); do not describe it as warning-free. No invalid byte application or order regression was observed in the measured runs.

`native-confirm/Player/ContinuousFrames.exe` contains the corrected expanded regression harness. `native-legacy` and `native-sequential` use this same executable with combined output disabled; the latter also disables prediction. Both passed the overlap regression. The sequential 60/60 measurement still missed 42 of 720 published frames (5.83%), demonstrating that two slots alone do not guarantee lossless operation when each image needs multiple waits.

The corresponding `udon-legacy` and `udon-sequential` client-target compiles and overlap regressions also passed. Legacy packing received 682/682 published frames; prediction-disabled decoding received 685/687 (0.29% missing), with sender deadline misses in both cases. These are compatibility checks with short measurements, not additional controlled throughput claims.

English, Korean and Japanese Docusaurus production builds completed with `npm run build`; the output is recorded in `website-build.log`. npm warned that the installed Node 21.2.0 was outside npm 11.1.0's supported range, but all three locale builds succeeded. Package/dependency versions were not changed to suppress that environment warning.

These tests do not cover an uploaded/live VRChat world, production scene serialization, OBS/Spout/video compression, IL2CPP, Quest, Linear color space or other graphics APIs. They cannot recover frames missing before decoder input, guarantee delivery of an RPC whose repeated copies were all lost, or bound a GPU/driver stall. Two retained slots cap memory/backlog count, not worst-case wall-clock latency.
