# Combined decoder byte output

## Implementation

Compatible payload shaders copy the already-decoded 14-pixel header prefix and decode the payload directly into the compact RGBA8 readback target. This removes the payload-intermediate write and separate packing draw. Source capture and optional calibration draws remain. Shaders opt in through `_TSMPHeaderTex` and `_TSMPHeaderPixels`; other plugins retain the old packing path. Custom fragments in the shipped codecs retain compilation with an older Core include through `TSMP_COMBINED_BYTE_OUTPUT` guards.

No wire-format, CRC, exact-length prediction, snapshot fallback, frame ordering or RPC repeat policy changed. `useCombinedByteOutput` is an advanced diagnostic opt-out. `combinedByteOutputCount` counts GPU submissions, not successfully applied frames. There are no claims of GPU milliseconds saved: this experiment measured delivery, not isolated GPU timestamps.

## Validation

Unity 2022.3.22f1, D3D11, RTX 4090, Gamma, Windows x64 Development Mono without the SDK; Worlds 3.10.4-beta.2 and bundled UdonSharp in the SDK VM. Both paths used automatic scheduling, real GPU callbacks and the actual file-referenced Core/codec packages. Runs were serial. Codec versions are the unreleased changes on top of Luma4 0.0.4-beta.1, RGB16 0.0.3-beta.3, RGB20/Color256 0.0.3-beta.4/0.0.3-beta.3 respectively.

The native enabled/control pair uses the same executable. Udon runs each perform client-target compilation. Both settings passed exact byte comparisons across four codecs and payload growth/shrink, same-snapshot fallback with live-source overwrites, sample changes, CRC rejection, cancellation/re-enable, twelve repeated RPCs applied once each, and two concurrent decoders. Regression combined-output counts were 73 enabled and 0 disabled in each runtime.

| Runtime / case | Separate packing received/published | Combined received/published | Separate / combined p95 latency |
| --- | --- | --- | --- |
| Native 60/60, 57-byte payload | 655/720 | 653/720 | 33.28 / 33.30 ms |
| Native 60/120, 57-byte payload | 720/720 | 720/720 | 16.67 / 16.67 ms |
| Native 60/120, 4121-byte payload | 720/720 | 720/720 | 16.69 / 16.68 ms |
| Udon 60/60, 57-byte payload | 681/717 | 657/695 | 33.14 / 33.19 ms |
| Udon 60/120, 57-byte payload | 720/720 | 720/720 | 16.80 / 8.71 ms |
| Udon 60/120, 4121-byte payload | 493/720 | 495/720 | 29.40 / 28.99 ms |

This comparison does not demonstrate a consistent reduction in frame loss. Differences in scheduling and the Udon sender's missed publication deadlines matter; do not infer a universal improvement from the 120-FPS latency row. The optimization removes a known draw/intermediate, but is not by itself a fix for readback waits spanning capture opportunities. Two-slot processing is a separate change and comparison.

The first native enabled launch failed the regression's ten-second readback timeout. A subsequent launch of the identical executable passed, as did the control. The initial failure is retained rather than silently discarded; its root cause was not isolated. No shader/runtime error appeared in its Player log. Neither cold-start immunity nor live VRChat compatibility is inferred from the successful rerun.

## Reproduction and artifacts

Set `TSMP_AUTOMATIC_SCHEDULING=1`. Run the normal wrapper with filter `prediction-regression,small-60-at-60,small-60-at-120,hd-large`. Compare `TSMP_DISABLE_COMBINED_OUTPUT=1` against `0` with prediction enabled. Repeat/alternate trials before drawing a performance conclusion on other hardware.

Compact evidence is in `Evidence/CombinedOutput`. Full logs/traces are under `F:/Unity/TSMP/Validation-Results/combined-output/`: `native-enabled` contains the initial failure and successful BuildReport/executable, `native-confirm` is the passing enabled rerun, `native-control` uses the same executable with the feature disabled, and `udon-enabled`/`udon-control` are compiled VM runs. No live VRChat client, OBS/video transport, IL2CPP, Quest or Linear color-space test was performed.
