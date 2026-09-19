# Continuous delivery results, 2026-09-19

## Finding

The current decoder already misses intermediate input images under sustained transmission. This is not only a theoretical margin concern. In the minimal Windows Player loopback, a 60 FPS game loop applied about 29 updates per second from a 60 Hz stream. A 120 FPS loop was close to, but did not reliably sustain, 60 distinct updates per second. Increasing the game-loop rate improved throughput; increasing the sender rate alone did not.

Latency stayed bounded rather than growing indefinitely. The decoder finishes the captured image and later samples a newer input; it does not queue every intervening input image. The cost is missing intermediate states.

No product C#, shader, protocol, package version, or runtime scheduling changes were made for this measurement. The tested Core source is `05bb03e372e44e183ff84af5d41622a8b685b26a`, whose Runtime matches release `v0.3.0-beta.2`.

## Environment

- Unity 2022.3.22f1, Direct3D 11, Gamma color space, NVIDIA RTX 4090, Intel Core i9-13900K.
- Native: actual Windows x64 Development Mono Player, stripping disabled. Build succeeded with zero errors and 16 shader warnings.
- Udon: Worlds 3.10.4-beta.2 and its bundled UdonSharp, client-target compilation, actual Encoder/Decoder/codec/probe bytecode in the SDK editor VM, real asynchronous VRC GPU readback callbacks.
- Codecs: Luma4 0.0.4-beta.1, RGB16 0.0.3-beta.3, RGB20 0.0.3-beta.4, Color256 0.0.3-beta.3.
- Direct Encoder RenderTexture to Decoder loopback. No OBS, Spout, MediaMTX, video decoder, compression, or network transport is present.
- One always-sent byte-array field, separate sender and receiver, sequence recorded inside the real receive callback. Empty camera scene, vSync off, explicit game-loop FPS targets, graphics enabled in batch mode.

These are same-process end-to-end measurements, not isolated decoder GPU timings. In particular, Udon encoding and observation overhead share the frame budget with the decoder. Neither a live VRChat client nor a populated world was tested.

## Confirmed Native Player

The following retest includes a completed preflight frame, a two-second continuous warm-up, a twelve-second measurement, and a one-second ongoing-stream observation tail. The tail prevents shutdown from classifying an otherwise receivable last frame as lost. Source: `Evidence/native-confirmed.csv`.

Luma4, 640x360, block size 8, sample size 1, 32-byte field / 57-byte network payload, 60 Hz actual publication:

| Actual loop FPS | Published | Applied measured IDs including tail | Applied/s inside window | Missing IDs | Latency p50 / p95 / p99, ms |
| --- | --- | --- | --- | --- | --- |
| 60.00 | 720 | 352 | 29.25 | 51.11% | 33.30 / 33.85 / 50.04 |
| 120.00 | 720 | 696 | 57.92 | 3.33% | 24.94 / 25.07 / 33.30 |
| 144.00 | 720 | 720 | 59.92 | 0% | 13.88 / 20.81 / 20.84 |
| 180.00 | 720 | 720 | 60.00 | 0% | 11.11 / 16.66 / 16.67 |
| 239.92 | 720 | 720 | 60.00 | 0% | 8.34 / 12.48 / 12.52 |

At 144 FPS one measured ID arrived after the measurement boundary, explaining the 59.92 within-window application rate despite all 720 IDs being received. Sender-only control published all 720 scheduled frames. Encoding p95 was approximately 0.56-0.59 ms for the small packet, much less than the observed readback-to-application delay.

The 60-second 60 FPS retest published 3,581 frames and applied 1,733 of those IDs, missing 51.61%. The source itself missed 19 scheduled publication slots; these are excluded from the decoder loss denominator. First-third versus last-third mean latency was 34.59 versus 34.61 ms. Latency p95/p99 was 49.96/50.01 ms, maximum 66.86 ms; latest-applied state age p95 was 67.20 ms. This is persistent reduced update rate, not an accumulating queue.

No decoder error observations, packet complement mismatches, duplicate applications, or out-of-order applications were recorded in these successful runs.

## Confirmed Udon Retest

The final Udon retest uses the same preflight and one-second observation tail as the confirmed Player measurement. Each row actually published 720 frames over twelve seconds. Source: `Evidence/udon-confirmed.csv`.

| Actual loop FPS | Applied measured IDs including tail | Applied/s inside window | Missing IDs | Latency p50 / p95 / p99, ms |
| --- | --- | --- | --- | --- |
| 60 | 349 | 29.00 | 51.53% | 33.27 / 49.87 / 50.00 |
| 120 | 716 | 59.58 | 0.56% | 16.66 / 24.92 / 25.06 |
| 144 | 720 | 60.00 | 0% | 13.86 / 14.00 / 20.82 |
| 180 | 720 | 60.00 | 0% | 11.13 / 11.23 / 16.74 |
| 240 | 720 | 60.00 | 0% | 8.36 / 12.52 / 12.64 |

Every confirmed Udon row had zero sender deadline misses, decoder error observations, corrupt applications and out-of-order applications. Encoding p95 was approximately 1.85-1.88 ms. At 60 FPS the maximum local application delay was 66.80 ms and the maximum application gap was 66.87 ms. At 120 FPS, small differences in callback scheduling change the missing percentage between repeats; the earlier full sweep missed 1.11%, and the native retest missed 3.33%. This remains a marginal configuration rather than proven 60 Hz delivery headroom.

## Wider Udon And Payload Sweep

`Evidence/udon-exploratory.csv` contains the full 21-case Udon sweep, including all codecs, source rates 15/30/60/90/120 Hz, loop targets through 240 FPS and uncapped, 32/1024/4096-byte fields, sample sizes 1/4, and 640x360/1280x720 images.

That exploratory sweep predates the one-second observation tail. Inputs near shutdown can be censored; use the confirmed retest for small loss percentages near zero rather than interpreting boundary differences as steady-state loss.

- Small Luma4 packets at a 60 FPS loop applied 29.58/s; a separate isolated run applied 29.17/s. At 120 FPS the full sweep applied 59.25/s, still below the 60 Hz source.
- Raising the source to 90 or 120 Hz at a 120 FPS loop left applications near 59/s. Sender rate is not decoder throughput.
- A 15 Hz source at a 60 FPS loop and a 30 Hz source at a 120 FPS loop were fully received. A 30 Hz source at 60 FPS was marginal rather than guaranteed.
- 1024-byte fields, sample 4, same-process 60 Hz source: Luma4 applied 59.58/s and RGB16 58.75/s at 120 FPS. RGB20 reached only 104.5 actual loop FPS and 29.92 applications/s; Color256 reached 82.08 FPS and 21.50 applications/s. Their encoding p95 was 9.76 and 13.10 ms respectively. These slower cases must not be attributed solely to snapshot copy or decoder shader cost.
- Udon Luma4 at 1280x720 applied 31.50/s for the small field even with a 120 FPS loop, and 23.33/s for the 4096-byte field with an 83.92 FPS loop. Higher resolution and sender work can change callback timing and available processing budget; the small-frame result is not a universal FPS threshold.
- The Udon 60-second case applied 1,787 of 3,448 published IDs. First/last-third mean latency was 34.84/35.02 ms. There was no progressive backlog, but intermediate input images were consistently missed.

The native exploratory sweep is retained in `Evidence/native-exploratory.csv`. Its first RGB16 sample-4 case did not sustain the requested source/loop rates, so it was repeated with explicit preflight. The confirmed RGB16 retest sustained 120 loop FPS and 60 Hz publication, applying 58.33/s and missing 2.64% of published IDs. Do not use the earlier exploratory row as a steady-state codec comparison.

## Interpretation And Limits

`DecodeNow` returns while `readbackInFlight` is true. Header readback completes before payload readback is submitted. Under the capped loops measured here, those sequential asynchronous stages frequently occupy two or more Unity frames. A roughly half-loop-rate ceiling in the small-packet saturated cases is consistent with that structure.

The decoder remained enabled throughout each measurement. `OnDisable` cancellation is therefore not the explanation for these gaps. Snapshot consistency must not be removed to hide the throughput problem: accepting a header and payload from different images is not valid reception.

The previous beta also had the single-in-flight guard and sequential readbacks. This measurement did not perform an old/new runtime A/B test, so it does not quantify a regression attributable specifically to the newly added copy.

Zero missing IDs in a short, lightly loaded run is not proof of adequate production headroom. There is still readback latency, scheduling jitter, and variable work. A live VRChat client, representative avatar workloads, real streamed video and different GPUs require separate measurements. The uncapped empty-scene results are not realistic VRChat FPS or a recommended workaround.

## Reproduction And Evidence

Use `Run-Validation.ps1` as described in `README.md`; its default matrix includes the boundary-safe observation tail. All full logs and per-ID traces for this session are under:

`F:/Unity/TSMP/Validation-Results/continuous-20260919/`

- `native-final/`: confirmed Player results, per-ID publication/application CSVs, Editor/Player logs, `Player/ContinuousFrames.exe`, and `Player/ContinuousFrames.build-report.txt`.
- `native-player2/`: full native exploratory sweep.
- `udon-final/`: full Udon exploratory sweep and client-compile/VM log.
- `udon-confirm/`: final five-case, boundary-safe Udon retest and per-ID publication/application traces.
- `udon-smoke3/`: independent successful 60 FPS Udon run.

For all twelve confirmed receiver cases, published IDs and received-ID membership were independently recomputed from the publication/application CSV traces. All counts and missing percentages matched the summary tables.

Preliminary unsuccessful harness runs are retained rather than counted as passes. These include an incorrectly oriented initial native fixture, a native multi-case setup/cleanup attempt without successful application, an editor domain-reload adapter initialization failure, and one Udon preflight watchdog timeout with its program counter at the return from `VRCGraphics.Blit`. Later isolated and full Udon sweeps completed without that timeout; its cold-start cause is not established, and this work does not claim to have fixed a product issue related to it. None of these failed runs contributes to the quantitative tables above.
