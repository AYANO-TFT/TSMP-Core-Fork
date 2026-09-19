# Predicted readback validation

## Result

The default-enabled fast path removes one sequential CPU/GPU readback wait when the current frame matches the previous validated decode configuration and payload length. It increases actual receiver applications and lowers local latency in these tests. It does not guarantee that a single-image decoder receives every source frame.

The final production implementation was built and executed in a Windows Player without the SDK and compiled/executed as client-target Udon bytecode in an SDK VM. Both passed the regression suite, including two concurrent decoders. This is not a live VRChat client result.

## Implementation and compatibility

- Capture one immutable Float32 source snapshot, as before.
- Initially use the existing header-then-payload readback sequence.
- After learning a header, run the normal header shader and the installed payload codec against the next snapshot using the previous settings. Pack their decoded bytes into one private RGBA8 target and request one readback.
- Validate the current header's magic, size, version and CRC, then apply the existing frame-order filter. Compare all static header metadata, exact payload length and resolved block/sample/start-block settings before accepting the speculative payload.
- On a valid-header mismatch, discard the speculative payload and decode the actual payload from the same snapshot. On a CRC failure, discard the frame without applying any speculative data.
- Manual layout, safety modes, unavailable packing material and an invalid prediction use the sequential path. Disabling the component releases owned resources and invalidates the prediction; the existing pending-readback cancellation guard remains active.
- Assign the material automatically through script defaults, editor/build preparation and native runtime resource loading. Udon uses the serialized material because its API does not expose the native material-copy constructor. Concurrent-decoder testing verifies that immediate property assignment and blit submission do not mix outputs in the tested environment.
- Keep the wire format, codec API, RPC repetition and duplicate suppression unchanged. No frame queue or parallel snapshot slots were added.

The combined buffer is internal: 56 header bytes followed by the predicted payload and only row padding. Its pixel width is `min(256, ceil((56 + payloadBytes) / 4))`. Header output uses a separate 14x1 target. The original image is not quantized to RGBA8; only recovered bytes are packed this way.

Exact-length matching is intentional. Asking an arbitrary plugin for a longer payload could change its layout or decoding behavior; prefix-stable decoding is not part of the codec contract. A changing payload length therefore sacrifices the fast path for correctness.

## Environment

- Date: 2026-09-19.
- Unity 2022.3.22f1, D3D11 with graphics enabled, Gamma color space.
- Intel Core i9-13900K, NVIDIA GeForce RTX 4090.
- Windows x64 Development Player, Mono, managed stripping disabled.
- VRCSDK Worlds 3.10.4-beta.2 and its bundled UdonSharp, client-target compile via `UdonSharpCompilerV1.CompileSync`.
- Codecs: Luma4 0.0.4-beta.1, RGB16 0.0.3-beta.3, RGB20 0.0.3-beta.4, Color256 0.0.3-beta.3.
- Isolated projects reference the actual Core and codec repositories through file dependencies. No benchmark processes ran concurrently.
- The measured working tree was based on `f0dce4a`; that revision in environment files is the base, not an assertion that the optimization was already committed. Final source hashes are retained in `Evidence/Prediction/source-hashes.json`.

## Small-payload A/B comparison

Luma4, 640x360, 32-byte value / 57-byte payload, sample size 1, 60 Hz publication target. These rows compare `*-control` (prediction disabled) with `*-confirm` (final implementation enabled). Each measurement lasts 12 seconds after warm-up.

| Runtime / loop target | Mode | Published | Received including tail | Apply Hz within window | Missing published frames | Local latency p95 |
|---|---|---:|---:|---:|---:|---:|
| Native Player / 60 FPS | Sequential | 720 | 348 | 28.92 | 51.67% | 49.96 ms |
| Native Player / 60 FPS | Predicted | 720 | 597 | 49.67 | 17.08% | 33.31 ms |
| Native Player / 120 FPS | Sequential | 720 | 686 | 57.08 | 4.72% | 25.11 ms |
| Native Player / 120 FPS | Predicted | 720 | 720 | 60.00 | 0% | 16.66 ms |
| Udon VM / 60 FPS | Sequential | 720 | 354 | 29.42 | 50.83% | 33.42 ms |
| Udon VM / 60 FPS | Predicted | 706 | 689 | 57.33 | 2.41% | 16.85 ms |
| Udon VM / 120 FPS | Sequential | 720 | 705 | 58.67 | 2.08% | 24.96 ms |
| Udon VM / 120 FPS | Predicted | 720 | 720 | 60.00 | 0% | 8.57 ms |

The enabled Udon 60 FPS run missed 14 sender scheduling slots and actually published at 58.83 Hz. Its missing percentage uses 706 actual publications, not 720 ideal slots. Apply Hz counts applications before the measurement window ends, whereas received counts include the observation tail. These are deliberately different quantities.

## Broader matrix and sustained run

Earlier exact-length runs are retained as `native-final` and `udon-final`. They predate the packing shader's equivalent integer-division warning cleanup and the added two-decoder/preparation tests. `native-confirm` and `udon-confirm` recompiled the final code and reran small-payload comparisons plus the expanded regression suite. The native matrix also predates explicit cache invalidation when entering the sequential path; none of its steady-state cases exercise that transition.

At a 120 FPS loop target and 60 Hz sender, the broader matrix measured:

| Case | Native apply Hz, sequential -> predicted | Udon apply Hz, sequential -> predicted | Udon missing, sequential -> predicted |
|---|---:|---:|---:|
| Luma4, 1024-byte value, sample 4 | Not paired / 60.00 | 58.67 -> 60.00 | 2.08% -> 0% |
| RGB16, 1024-byte value, sample 4 | Not paired / 60.00 | 58.33 -> 60.00 | 2.64% -> 0% |
| RGB20, 1024-byte value, sample 4 | Not paired / 60.00 | 30.50 -> 59.92 | 49.03% -> 0% |
| Color256, 1024-byte value, sample 4 | Not paired / 59.92 | 23.33 -> 43.75 | 60.97% -> 26.94% |
| Luma4, 1280x720, 32-byte value | 55.92 -> 60.00 | 32.50 -> 59.92 | 45.69% -> 0% |
| Luma4, 1280x720, 4096-byte value | 56.33 -> 60.00 | 24.25 -> 40.50 | 59.44% -> 32.36% |

Heavy Udon cases also execute the encoder in the same process and do not sustain their 120 FPS loop target. In the enabled run, Color256 averaged 79.25 FPS and HD-large 77.83 FPS. These are end-to-end observations, not isolated decoder GPU timings. Core contains no codec-specific fast-path branches; installed codec implementations are exercised unchanged.

The 60-second, 60 FPS small-payload test produced:

| Runtime | Sequential apply Hz | Predicted apply Hz | Predicted received / published | Predicted first/last-third mean latency |
|---|---:|---:|---:|---:|
| Native | 28.83 | 51.85 | 3112 / 3600 | 19.41 / 19.13 ms |
| Udon VM | 29.57 | 55.75 | 3346 / 3405 | 16.91 / 17.00 ms |

No growing application backlog was observed. The Udon sender missed 195 scheduling slots in the enabled sustained run and actually published at 56.75 Hz. All retained throughput runs reported zero decoder-error observations, corrupt packet markers and out-of-order applications. An independent publication/application trace audit checked counts, membership and uniqueness for all 38 retained measurement cases; see `trace-audit.csv`.

## Regression checks

Both final native Player and compiled Udon runs passed:

1. Switch among all four shipped codecs and back to Luma4.
2. Change value lengths through 32, 33, 1024 and 4096 bytes in both directions, including multi-row combined readback. Compare the entire received byte array, not just its frame marker.
3. Overwrite the live source immediately after submitting each decode. The received packet must remain the captured one, including on prediction fallback.
4. Change header sample size between 1 and 4.
5. Damage a current header CRC symbol. No variable application is permitted; the decoder must report the CRC mismatch. Recover with valid frames afterward.
6. Disable/re-enable while a combined readback is pending. Discard that result and accept the next frame.
7. Send 12 RPC events, intentionally skip two of each four repeated frame copies, and verify one execution per event. This does not prove delivery if all repeated copies are lost.
8. Run two decoders concurrently with different codecs, source dimensions and payload lengths; verify both complete byte arrays independently.

Each final regression run recorded 36 accepted predictions and 35 fallbacks. The Udon editor check additionally cleared a decoder's material reference, invoked the normal automatic setup preparation, and verified that the material was both assigned and serialized into the backing `UdonBehaviour`.

The final native BuildReport is Succeeded with 0 errors and 16 existing codec shader warnings. No packing-shader warning remains. The initial Udon attempt exposed the unsupported material-copy constructor; it was replaced with the serialized-material path before the successful compile and runtime checks.

## Reproduction and artifacts

Use `Run-Validation.ps1` as documented in [README](README.md). Set `TSMP_DISABLE_PREDICTION=1` for the control; remove it for enabled runs. Do not select `prediction-regression` with prediction disabled.

Final confirmation filter:

```text
prediction-regression,small-60-at-60,small-60-at-120
```

Broad matrix filter:

```text
prediction-regression,small-60-at-60,small-60-at-120,small-60-at-144,codec-0-sample4,codec-1-sample4,codec-2-sample4,codec-3-sample4,hd-small,hd-large,sustained-60
```

Repository evidence: `Validation~/ContinuousFrames/Evidence/Prediction/`. It includes summaries, statuses, regression results, build summaries, environment records, preparation verification, source hashes and trace-count audit. Raw per-frame publication/application traces and full logs remain under `F:/Unity/TSMP/Validation-Results/predicted-readback/<run>/`.

- Final Player: `native-confirm/Player/ContinuousFrames.exe` under that artifact root.
- Player build log: `native-confirm/Editor.log`.
- Player runtime log: `native-confirm/Player.log`.
- Final Udon compiler/runtime log: `udon-confirm/Editor.log`.
- Udon serialization check: `udon-confirm/preparation.txt`.

`npm run build` also succeeded for the English, Korean and Japanese documentation. The npm/Node version compatibility warning was non-fatal; documentation packages were not upgraded.

## Limits

No live VRChat world/client, OBS/Spout/MediaMTX transport, Quest/mobile GPU, Linear color-space run, IL2CPP Player or loaded multi-avatar world was tested here. The VM adapter uses real compiled Udon instructions and real asynchronous VRC GPU callbacks but includes host instrumentation. The automatic material-serialization check is not a complete VRChat world build. Hardware scheduling and frame phase influence the results, especially at 60 FPS.

The optimization adds two small private byte targets and one GPU packing pass. It can waste speculative GPU work on duplicate, corrupt or changed frames; it never treats that work as permission to apply them. Rapidly changing payload lengths or configuration can retain the original two-wait latency and add speculative work. Disable `Use Predicted Readback` under Diagnostics > Advanced Decode to compare a particular workload. RPC recovery still depends on at least one valid repeated copy being received within its existing repeat budget.
