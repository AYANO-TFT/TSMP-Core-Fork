# Continuous frame delivery measurement

This harness measures the current, unmodified Encoder and Decoder under a continuously changing source. It does not wait for a decode to finish before publishing another frame. `PASS` means that the measurement completed without invalid application data, not that all frames were delivered.

## Method

- A real Encoder serializes one always-sent `TransSync` byte-array field and renders through an installed codec. The Decoder reads that output RenderTexture directly.
- The packet carries a monotonically increasing number and complementary check bytes. The receiver records the number and a local monotonic timestamp inside `OnTSMPVariableReceived`, after the real binding dispatcher has assigned the decoded value.
- Encoder and Decoder run once per eligible Unity frame. Both are manually invoked by the harness to establish a repeatable publish-before-decode order; neither implementation is replaced. `autoEncode` and `applyEveryFrame` are off only to prevent duplicate automatic invocations.
- Publication is wall-clock paced, at most once per Unity frame. Missed sender deadlines are counted separately from published frames that were never applied. There is no catch-up burst that could artificially count several overwritten images as distinct visible frames.
- Each case first completes one preflight frame to exclude first-use shader creation, then continuously warms up for two seconds and measures for twelve seconds (sixty for `sustained-60`). It keeps publishing and decoding for one more second, without counting the new publications, so the last measured IDs have an ordinary opportunity to be decoded instead of being censored by shutdown. It then stops publishing and drains only the pending decode.
- Frame loss is the fraction of actually published, measured-window IDs absent from receiver callbacks, including the observation tail and final pending callback. Apply Hz counts measured-window IDs applied before the window ends and can consequently be slightly below the sender rate even with zero missing IDs. Publication and application traces retain both sides of this boundary.
- End-to-end local latency is receiver callback time minus the time immediately before encoding that ID. Snapshot latency starts immediately before its first decode request. Source age samples the age of the latest applied value each Unity frame and includes the hold time between applications.
- The first/last-third latency means expose growing backlog. Application-gap percentiles expose uneven motion updates even if mean latency stays bounded.
- Source and receiver use the same machine's `Time.realtimeSinceStartup` clock. The Udon-compatible callback timestamp is float precision; host scheduling uses its double-precision counterpart.
- The sender-only control deliberately has no receiver. Its missing/apply columns are not a delivery result.

## Execution

Use isolated Unity 2022.3.22f1 projects with file dependencies referencing the actual Core and four codec repositories. Do not use `-nographics`. Only run one benchmark process at a time.

```powershell
./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/continuous/native `
  -Mode Player

./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-VRC `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/continuous/udon `
  -Mode Udon
```

`-Mode Play` runs the native path in Editor. `-Filter small-60-at-60,small-60-at-120` selects cases. Every run writes `environment.txt`, `summary.csv`, per-case application traces, and `status.txt`; the wrapper also records Editor/Player logs and the Player BuildReport summary.

The Udon adapter performs a full client-target UdonSharp compilation, loads the actual Encoder, Decoder, codec and probe programs into the SDK VM, and uses real VRC GPU readback callbacks. It does not manually synthesize or complete readbacks. It constructs backing behaviours explicitly for instrumentation and therefore is not a production scene serialization test or a live VRChat client test.

## Interpretation

Compare `txHz` with `applyHz`, not the configured target alone. `schedulerMisses` can indicate that the source itself could not sustain the requested rate. `busyPercent` counts render ticks at which a new decode could not start; it is not GPU utilization. `captureCount` includes duplicate-image requests. `decoderErrorObservations` records failing decoder diagnostics observed between requests, independently of callback delivery gaps.

`valueBytes` excludes TSMP message headers; use `payloadBytes` for the encoded payload length. The four sample-4 cases use the shipped codec prefab settings, including RGB16 4/4/4 Refine and Color256 Robust Refine.

This is a minimal local loopback with one field and a lightweight receiver, not an OBS/Spout/MediaMTX, video compression, multi-avatar, Quest, or live VRChat benchmark. Host VM dispatch and heap observation add measurement overhead. A low-loss result on this machine is not a universal guarantee or proof that a populated world has sufficient headroom.
