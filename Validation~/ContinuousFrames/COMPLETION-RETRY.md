# Completion-triggered retry investigation

## Decision

Do not add completion-triggered automatic retries to the production decoder on the basis of this experiment. The proposed scheduling gap was not reproduced: after preflight, every observed terminal readback completion was visible before the host's next `Update` sample in the same Unity frame. The candidate's retry counter remained zero in all enabled runs. There is no demonstrated delivery benefit.

The candidate was implemented, compiled and executed, then removed from `Packages/`. It is archived as `Experiments/CompletionRetry.patch` for reproducibility, not imported by Unity. The production decoder, inspector, protocol and settings are unchanged. The retained improvements are automatic-mode measurement, optional timing observation and an independent trace audit.

## Hypothesis and candidate

The hypothesis was that `Update` could find a pending readback, return, then receive its completion later in that same frame and leave the decoder idle until the next `Update`.

The experimental overlay:

- Records the frame of the automatic request and the most recent capture.
- After the current callback finishes parsing/applying, retries a missed automatic request from that frame if no subsequent readback is pending.
- Limits automatic capture to once per Unity frame, does not turn manual calls into an automatic loop, and clears eligibility on disable.
- Guards against reentrant `DecodeNow` while the current callback is applying values.
- Adds capture/completion timestamps and counters for observation, plus a retry switch for paired tests.

`native-control` and `udon-control` use the experimental overlay with retries disabled. They are not unmodified-production builds: both sides include its instrumentation and guards. `*-enabled` changes only the retry switch. The native pair uses the same Player executable.

## Method

Measured on 2026-09-19 with Unity 2022.3.22f1, D3D11, Gamma, NVIDIA RTX 4090 and Intel Core i9-13900K. Native tests use a Windows x64 Development Mono Player without VRCSDK, stripping disabled. Udon tests use Worlds 3.10.4-beta.2 and bundled UdonSharp, client-target compilation, the SDK VM and real VRC GPU readback callbacks. This is not a live VRChat client test.

The codec is Luma4 0.0.4-beta.1, 640x360, sample size 1, 32-byte value / 57-byte payload. The existing predicted readback optimization remains enabled. Each case warms up for two seconds, measures twelve seconds and continues publication for a one-second observation tail.

The revised harness publishes from an early `Update`. The native decoder runs its ordinary `Update`; the SDK VM adapter invokes the compiled `_update` entry point from the host Update. It records optional state samples in host `Update` and `LateUpdate`. Timestamps inside the candidate identify the Unity frame in which the callback actually finished. Preflight is excluded from phase classification because observation begins after it has already finished.

The audit checks actual publication/application ID membership and uniqueness, count agreement with summaries, per-frame capture limits, and the phase in which each new completion timestamp was first seen. A missing publication being observed while busy establishes an observed overlap, not a measurement of GPU shader duration or a distinction between GPU completion and callback-delivery delay.

## Paired results

All cases published their requested count. Apply Hz counts applications inside the measurement window; received counts also include the observation tail.

| Runtime / sender / loop | Retries | Received / published | Apply Hz | Missing | Latency p95 | Retry executions |
|---|---|---:|---:|---:|---:|---:|
| Native / 60 Hz / 60 FPS | Disabled | 705 / 720 | 58.67 | 2.08% | 16.71 ms | 0 |
| Native / 60 Hz / 60 FPS | Enabled | 694 / 720 | 57.75 | 3.61% | 17.17 ms | 0 |
| Native / 60 Hz / 120 FPS | Disabled | 720 / 720 | 60.00 | 0% | 8.37 ms | 0 |
| Native / 60 Hz / 120 FPS | Enabled | 720 / 720 | 60.00 | 0% | 8.37 ms | 0 |
| Udon VM / 60 Hz / 60 FPS | Disabled | 704 / 720 | 58.58 | 2.22% | 16.73 ms | 0 |
| Udon VM / 60 Hz / 60 FPS | Enabled | 695 / 720 | 57.83 | 3.47% | 16.87 ms | 0 |
| Udon VM / 60 Hz / 120 FPS | Disabled | 720 / 720 | 60.00 | 0% | 8.63 ms | 0 |
| Udon VM / 60 Hz / 120 FPS | Enabled | 720 / 720 | 60.00 | 0% | 8.61 ms | 0 |

Both runtimes also received all 360 publications at a 30 Hz sender / 60 FPS loop, with retries either enabled or disabled. No retry executed there either. The small differences between runs are not evidence of an effect of the retry path: that path never started a capture.

After excluding preflight, the phase audit found zero new completions first appearing at `LateUpdate` or after the preceding frame's `LateUpdate`. Every missing measured ID in the instrumented 60/60 cases was observed at an Update while readback remained pending. There were no corrupt marker values, out-of-order applications, prediction fallbacks or decoder-error observations in these throughput cases.

Observed completion-to-next-capture p95 at 60/60 was 0.85 ms for native and 2.02-2.12 ms for Udon. This interval includes the sender's encoding work before the next capture. It is not a whole unused frame, nor is it isolated GPU time.

## Production recheck

The overlay was removed, the final harness made tolerant of absent instrumentation, and the unchanged production decoder was rebuilt/recompiled and rerun:

| Runtime / loop | Actually published | Received including tail | Apply Hz | Missing |
|---|---:|---:|---:|---:|
| Native / 60 FPS | 719 | 607 | 50.50 | 15.58% |
| Native / 120 FPS | 720 | 720 | 60.00 | 0% |
| Udon VM / 60 FPS | 695 | 685 | 57.00 | 1.44% |
| Udon VM / 120 FPS | 720 | 720 | 60.00 | 0% |

The native 60 FPS run missed two sender scheduling slots; Udon missed 25. Missing percentages use actual publications. The native production result differs substantially from the instrumented pair. These runs also follow the full regression suite and omit timing instrumentation. Do not call that difference a production optimization, or assign it to a specific instrumentation/warm-up/phase effect without a separate controlled experiment. The earlier predicted-readback report used a manual coroutine driver and is likewise not directly comparable to this automatic-mode experiment.

Both final production runs passed the existing full regression suite: codec changes, exact full-byte comparisons across payload growth/shrink, source overwrites during pending reads, sample-size changes, CRC rejection, disable/enable cancellation, 12 repeated RPC events executed once each and concurrent decoders. The Udon run also passed automatic packing-material serialization. The native Player BuildReport succeeded with zero errors and 16 existing codec shader warnings. Udon client-target compilation and execution completed without errors.

## Reproduce

The ordinary production test needs no source patch:

```powershell
$env:TSMP_AUTOMATIC_SCHEDULING = '1'
./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/completion-retry/native-production `
  -Mode Player -Filter 'prediction-regression,small-60-at-60,small-60-at-120'
```

Use the isolated SDK project and `-Mode Udon` for bytecode execution. Remove `TSMP_AUTOMATIC_SCHEDULING` for the earlier manual coroutine driver.

To reproduce the rejected candidate, first inspect the overlay against a clean checkout of the tested base `9b743ed`. Do not apply it over unrelated local decoder edits. Run only one benchmark at a time, and close validation Editors before reversing the overlay.

```powershell
git apply --check Validation~/ContinuousFrames/Experiments/CompletionRetry.patch
git apply Validation~/ContinuousFrames/Experiments/CompletionRetry.patch
$env:TSMP_AUTOMATIC_SCHEDULING = '1'
$env:TSMP_DISABLE_READBACK_RETRY = '1'
# Run the wrapper with filter small-60-at-60,small-60-at-120,small-30-at-60.
$env:TSMP_DISABLE_READBACK_RETRY = '0'
# Repeat in a different result directory.
git apply -R Validation~/ContinuousFrames/Experiments/CompletionRetry.patch
./Validation~/ContinuousFrames/Audit-Scheduling.ps1 `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/completion-retry
```

The patched decoder fields are not part of the supported Scripting API. The overlay retains normal unified-diff context, including blank context lines.

## Artifacts and scope

Compact evidence is checked in under `Evidence/Scheduling/`: environment, summary, status, build and regression files, scheduling summaries and the independent audit. Full logs and per-frame traces are at `F:/Unity/TSMP/Validation-Results/completion-retry/<run>/`.

- Final native executable: `native-production/Player/ContinuousFrames.exe`.
- Native build/runtime logs: `native-production/Editor.log`, `native-production/Player.log`.
- Udon compile/runtime log: `udon-production/Editor.log`.
- Candidate native executable, used for both switch settings: `native-control/Player/ContinuousFrames.exe`.
- Candidate phase traces: `*-timing.csv` in each control/enabled run.

No live VRChat, video-transport, Quest, Linear-color-space or IL2CPP test was performed. The candidate branch requiring a completion after a blocked Update was never naturally exercised here and is not certified for other scheduling environments. The observed remaining gaps are consistent with a readback still being pending at the next capture opportunity; this experiment does not isolate the GPU, render-thread, driver or callback scheduler contribution. Investigate those costs or bounded overlap separately instead of shipping an unproven retry option.
