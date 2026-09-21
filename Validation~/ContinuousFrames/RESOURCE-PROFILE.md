# CPU, GPU and memory profile

## Scope and environment

Measured on 2026-09-19 against Core `b80194c` (package version `0.3.0-beta.2`) and Luma4 `30f422c` (`0.0.4-beta.1`). Production code was not changed. Only the validation harness has instrumentation.

- Unity 2022.3.22f1, Windows 11, i9-13900K, 32 GB system RAM, RTX 4090, D3D11, Gamma.
- Native: Windows x64 Development Player, Mono, managed stripping disabled. Build succeeded with zero errors and 16 shader warnings. Batch-mode graphics were active, not `-nographics`; the device reported single-threaded graphics submission.
- Udon: Worlds 3.10.4-beta.2 and its bundled UdonSharp. All 24 client-target programs compiled. The real SDK VM and real GPU callbacks were used in Editor, not a live VRChat client. Editor/VM overhead and the client scripting environment differ.
- Luma4, block size 8, one always-sent byte-array field. A 32-byte value produces a 57-byte payload; a 4096-byte value produces a 4121-byte payload. Sample size 1 unless explicitly indicated.
- Prediction, combined byte output and two-slot overlap enabled, except the explicit one-slot control. One publication and `DecodeNow` call per early Unity Update, no deadline catch-up. This is explicit submission profiling, not a repeat of the automatic-scheduling delivery benchmark.
- Each full case warms for three seconds, measures twelve seconds, continues publishing for a one-second observation tail, then drains pending requests. GPU microbenchmarks and helper measurements run after delivery stops.
- No OBS/Spout/video decoding, real avatars, IK, transform/humanoid capture, interpolation or application-side rendering. One unrelated idle Unity editor was left open; this is a workstation measurement, not a fully isolated hardware laboratory run.

## Main findings

The largest measured CPU costs are in the encoder, not the new readback slots. Udon executes an image-size-dependent symbol-buffer copy and a payload-size-dependent raster loop. Native Luma4 allocates and fills a full-resolution managed image every frame. Decoder snapshots dominate retained texture memory even for tiny payloads. On this GPU, the measured decoder draw sequence is much cheaper than those CPU paths.

### CPU: Udon

Times below are mean milliseconds per call to the real VM event, including nested codec/receiver calls. Submission and completion are distinct calls. Do not add the nested rows in `cpu.csv` again. The VM decorator only times `Interpret` and delegates the original interface operations unchanged.

| Source / value / target | Encoder | Decode submission | Readback callback | Actual loop Hz |
| --- | ---: | ---: | ---: | ---: |
| 640x360 / 32 B / 30 | 1.72 | 0.37 | 0.49 | 30.06 |
| 640x360 / 32 B / 60 | 1.71 | 0.36 | 0.48 | 60.07 |
| 1280x720 / 4096 B / 30 | 10.97 | 0.37 | 3.47 | 30.04 |
| 1280x720 / 4096 B / 60 | 11.27 | 0.38 | 3.52 | 59.83 |
| 1920x1080 / 32 B / 60 | 10.74 | 0.37 | 0.48 | 60.00 |
| 3840x2160 / 32 B / 30 | 40.51 | 0.39 | 0.48 | 23.53 |
| 1280x720 / 4096 B / 60, sample 4 | 11.31 | 0.45 | 3.50 | 59.71 |

At 720p/4096 B, the sum of mean encode/submission/completion costs is about **15.17 ms**, about **91% of a 16.67 ms frame budget** when both endpoints run in the same process. This is not a measured p95 frame time, and excludes scene work. A receiver-only process does not pay the encoder portion. Encoder p95 was 12.10 ms and callback p95 3.69 ms. Delivery of all 718 published IDs does not imply comfortable CPU headroom.

At 4K, all 283 published IDs were received, but the source itself only ran at 23.53 Hz despite the 30-Hz target. Lossless delivery of published data and sustaining the intended capture rate are different properties.

#### Encoder causes

`TSMPEncoder.WriteFrameTexture` calls `EncoderUdonTextureRuntime.CopyPixelBuffer(_basePixels, _pixels)` every frame. The function copies every `Color32` using a Udon loop. With block-size 8 the loop covers 3,600 elements at 640x360, 14,400 at 720p, 32,400 at 1080p and 129,600 at 4K, even for a 32-byte value. Cached static pixels avoid rebuilding the pattern but do not avoid this copy.

A separate warmed microbenchmark executing the actual production helper in compiled Udon measured:

| Helper | Workload | Mean ms |
| --- | --- | ---: |
| `CopyPixelBuffer` | 720p symbol buffer | 4.48 |
| `CopyPixelBuffer` | 1080p symbol buffer | 9.98 |
| `CopyPixelBuffer` | 4K symbol buffer | 40.52 |
| `FrameRaster.WriteLuma4BytesToBlockTexture` | 4096 bytes, 720p | 4.65 |
| `NetworkValueWriter.WriteRawBytes` | 4096 bytes | 1.59 |

The near-empty control event was about 0.45 microseconds. The measurements confirm the loops' scaling, but are not exclusive samples from the same frame: do not subtract or sum them as an exact decomposition of `EncodeNow`. Cache state, dispatch and CPU clock behavior differ.

#### Decoder causes

`CompletePredictedReadback` converts the RGBA byte texture back into `_payloadBytes` through `ByteTextureReader.CopyBytesAtPixel`. The variable dispatcher subsequently copies the value into the receiver's cached array through `NetworkValueReader.CopyRawBytes`.

For 4096 bytes these helpers separately averaged **1.36 ms** and **1.53 ms** in the Udon microbenchmark. The callback averaged **3.52 ms** in continuous operation. These two byte-wise loops explain much of the payload-size dependence; header CRC is smaller at about **0.042 ms**. Codec option dispatch was about 1 microsecond per call, despite six calls per frame. Removing codec extensibility is not justified by this profile.

### CPU and GC: native Unity

The native encoder calls `Luma4Raster.TryWriteFrame`, which calls `FrameRaster.CreateClearedPixels(width, height)`. This allocates `new Color32[width * height]`, fills the whole image, writes the occupied blocks, and uploads the whole texture through `SetPixels32` / `Apply`. It does not use the Udon block-symbol image path.

| Source / value / target | Encode mean ms | Encode p95 ms | Median frame GC allocation | Image-array allocation rate at target |
| --- | ---: | ---: | ---: | ---: |
| 640x360 / 32 B / 60 | 0.50 | 0.57 | 922,164 B | 52.73 MiB/s |
| 1280x720 / 4096 B / 60 | 3.33 | 3.63 | 3,686,964 B | 210.94 MiB/s |
| 1920x1080 / 32 B / 60 | 3.56 | 4.50 | 8,294,964 B | 474.61 MiB/s |
| 3840x2160 / 32 B / 30 | 20.72 | 23.45 | 33,178,164 B | 949.22 MiB/s |

Frame GC numbers are Unity's whole-process `GC Allocated In Frame` counter, not scoped TSMP-only allocations. Their dominant term matches `width * height * 4`; the source supplies the attribution. In the twelve-second 720p/60 test, `GC.CollectionCount(0)` increased by 720. Temporary image allocation is churn, not evidence of a retained-memory leak.

Native decode submission averaged about 0.08-0.14 ms. `EarlyUpdate.UpdateAsyncReadbackManager` averaged about 0.11 ms at 720p and 0.16 ms at 4K. That engine marker includes completion plumbing and callbacks and is not an exclusive TSMP method measurement.

**Measurement guard:** Unity's Mono implementation returned zero from `GC.GetAllocatedBytesForCurrentThread` even for a known 8192-byte allocation. Scoped byte counts are therefore explicitly **unavailable (-1)**, not zero. Only the verified Unity frame counter is used for allocation conclusions. The Udon Editor's global counters and changing editor heap are not used to claim zero allocation or a TSMP memory leak.

### Retained memory

`DecoderSnapshotRuntime.Capture` always uses linear `ARGBFloat` (16 bytes/pixel). Two independently retained snapshots are required by the current two-slot implementation.

| Resolution | Snapshot storage per decoder, two slots |
| --- | ---: |
| 640x360 | 7.03 MiB |
| 1280x720 | 28.13 MiB |
| 1920x1080 | 63.28 MiB |
| 3840x2160 | 253.13 MiB |

These are logical texture storage sizes, confirmed against live format/dimension inventory and Unity runtime-size estimates. They are not total driver VRAM residency. Encoder output/staging textures, video textures, render targets, readback staging, materials and runtime heaps are additional. Multiple decoders multiply the snapshot cost.

Warm/end inventories show no growth of these textures or selected decoder buffers during a fixed-layout case. Byte-array and readback caches are small relative to the snapshots. This short test does not establish leak freedom across repeated layout changes or long VRChat sessions.

**One-slot option does not currently halve memory.** A fresh instance with `overlapReadbacks=false` still allocated both snapshots. `DecodeNow` chooses `(_slotHead + pendingDecodeCount) % 2`, and completion advances `_slotHead` modulo two even when admission is limited to one request. Successive captures populate both retained buffer sets. The control received 651/721 small frames and 632/721 large frames while retaining the same snapshot storage. Disabling overlap is not a memory optimization in this implementation.

### GPU

The first Player and Editor timestamp attempts were unusable: some markers were zero or stale. They are excluded. Valid results came from a dedicated native Editor pass with explicit camera rendering to advance GPU frames, positive GPU timestamps and exactly one recorded GPU marker block. Source, snapshot, header and combined-output materials/textures came from the warmed production loopback. Replayed output was checked against the production-decoded payload.

Each sample is 32 repeated sequences, divided by 32; 15 warm frames and 25 measured frames. LUT preparation is included before both header and payload draws for sample size 4. CPU preparation, encoder upload, readback transfer/latency and other world rendering are **not included**.

| Resolution / payload / sample | Snapshot-only median us | Complete decoder draw sequence median us | Sequence p95 us |
| --- | ---: | ---: | ---: |
| 640x360 / 57 B / 1 | 8.10 | 191.20 | 191.42 |
| 1280x720 / 4121 B / 1 | 30.56 | 105.59 | 105.95 |
| 1920x1080 / 57 B / 1 | 68.06 | 136.32 | 228.00 |
| 3840x2160 / 57 B / 1 | 79.65 | 197.92 | 198.14 |
| 1280x720 / 4121 B / 4 | 30.46 | 117.54 | 117.79 |

The complete sequence alternates source-copy, header and payload resources; isolated repeated-pass times are not additive. GPU clocks, batching and resource dependencies make these microbenchmark rows non-monotonic. Do not infer that larger resolutions are faster, or that a complete world costs only 0.1-0.2 ms. The defensible result is that this RTX 4090's measured decoder draws are much smaller than the observed Udon CPU costs. One sample per pixel is not representative of every custom codec, Refine mode, weaker GPU or Quest.

The full-image snapshot still reads RGBA32 and writes RGBAFloat on each capture. A nominal 1080p capture moves at least 39.55 MiB of source/destination texel data, about 2.32 GiB/s at 60 captures/s; 4K at 30 is about 4.63 GiB/s. These are format-derived traffic estimates, not measured DRAM traffic, and ignore caches/compression. Two slots do not imply two copies for each source image, but allow more images to be admitted than a saturated single-slot decoder.

## Optimization order

1. **Udon encoder whole-symbol-buffer copy:** evaluate exposed bulk-copy APIs or restoring only previously written spans from the cached base image. Preserve removal/shrinking-payload/codec-switch behavior so old blocks do not remain visible. This is the strongest resolution-dependent CPU bottleneck.
2. **Udon byte processing and Luma4 raster loops:** evaluate bulk array copy where actually exposed in the client, then reduce per-byte operations and avoid redundant intermediate value copies where ownership permits. Validate real Udon compilation and buffer isolation; do not replace supported code with unverified generic calls.
3. **Native Luma4 allocation:** reuse raster storage and consider the existing block-symbol rendering strategy rather than allocate/upload a full-resolution image every frame. Preserve codec-plugin contracts and both Gamma/Linear output behavior.
4. **Snapshot memory:** investigate format-aware capture for inputs that do not need Float32, retaining Float32 when required. Bit accuracy, sRGB/linear conversion, HDR and high-precision/custom codecs need regression coverage before changing the default. Also make one-slot admission actually reuse one storage slot if a lower-memory mode is desired.
5. **GPU pass tuning:** lower priority on this hardware than the preceding CPU/GC/memory work. Test weaker GPUs and production codecs before generalizing.

No optimization or protocol change was applied in this measurement task.

## Reproduction and evidence

Compact evidence is in [Evidence/ResourceProfile](Evidence/ResourceProfile). Raw logs, the native executable and preliminary/valid measurements are under `F:/Unity/TSMP/Validation-Results/resource-profile/` on the measurement machine. The native executable is `native-v2/Player/ContinuousFrames.exe`; its adjacent BuildReport records the successful build. `udon-v2/Editor.log` records full client-target compilation; `gpu-editor-v2` contains timestamp traces and replay checks.

```powershell
./Validation~/ContinuousFrames/Run-Validation.ps1 `
  -ProjectPath F:/Unity/TSMP/Validation-Codecs-NoSDK `
  -ResultsDirectory F:/Unity/TSMP/Validation-Results/resource-profile/native-v2 `
  -Mode Player `
  -Filter 'profile-small30,profile-small60,profile-large30,profile-large60,profile-1080-small60,profile-4k-small30,profile-luma4-sample4'
```

Repeat using `Validation-Codecs-VRC`, `-Mode Udon` and a separate result directory for compiled VM measurements. Use `$env:TSMP_SINGLE_SLOT='1'` only for the one-slot control, then remove it. The native control used the identical previously built executable, not a different build.

For GPU timing, use `-Mode Play`, `$env:TSMP_PROFILE_GPU_ONLY='1'` and the five cases listed in the GPU table. This reduces the delivery window to 0.5 seconds; those short delivery/CPU numbers are not mixed into the full-run results. Remove the variable before a normal measurement. Run one benchmark process at a time.

`Export-ResourceEvidence.ps1` combines the selected run directories into the compact CSV evidence. `cpu.csv` contains inclusive root and nested scopes; `helpers.csv` is a separate production-helper microbenchmark. `counters.csv` contains whole-process Unity counters, with time counters in nanoseconds and byte counters in bytes. `memory-*.csv` inventories selected decoder storage only plus the shared encoder output. `delivery.csv` distinguishes actually published IDs from configured target FPS. GPU traces retain sample counts and timestamps so unsupported or stale recordings can be rejected.

Instrumentation uses Unity's [ProfilerRecorder](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.html), GPU `CustomSampler`/`Recorder`, runtime texture-size inventory and `Stopwatch`. Runtime generation, shaders, codec IDs, frame format and production component methods remain unchanged. Udon assets reserialized by compilation are backed up outside the repository and excluded from the commit.
