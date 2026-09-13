# Variable-length payload buffers (#17)

Validated on 2026-09-13 with Unity 2022.3.22f1, Windows D3D11, RTX 4090; SDK tests use 3.10.4-beta.2 and full client UdonSharp compilation.

## Allocation measurements

After 100 warm-up requests, alternate 2048 and 2065 bytes for 10,000 calls. `ProfilerRecorder` records `GC.Alloc` event counts on the current thread. Track replaced array identities and sum their lengths separately to report allocated array data, excluding object headers. This is a helper microbenchmark, not total encoder/decoder frame allocation or VRChat frame time.

| Path | Before GC.Alloc events | After events | Before array data bytes | After array data bytes |
| --- | ---: | ---: | ---: | ---: |
| Decoder EnsureByteBuffer | 10,000 | 0 | 20,565,000 | 0 |
| Native CopyPrefix (unchanged control) | 10,000 | 10,000 | 20,565,000 | 20,565,000 |

The selected change is private decoder scratch storage: preserve high-water capacity until destruction and use `_payloadDataBytes` as the valid length. Header-layout parsing and readback requests both use the same allocation policy. Diagnostics report valid bytes, not capacity. An empty header payload no longer inherits the previous frame's length.

`CopyPrefix` and outgoing avatar packet arrays still require exact lengths. The plugin `TryWriteFrame` API and RawBytes fields consume array length; switching them to oversized arrays would change data seen by plugins/receivers. Those allocations are deliberately retained. Per-binding received arrays remain independent and exact-sized. This change does not claim allocation-free TSMP or alter public byte-array ownership, packet layout, codecs or interpolation.

## Verification

- SDK-free tests: initial empty buffer, shrink/grow, 65,535-byte capacity, reuse, truncated header/body, dirty unused tail, trailing bytes within the valid payload, oversized valid length and zero header payload.
- Actual Udon helper bytecode: empty/shrink/grow/max, object identity and retained RawBytes field isolation.
- SDK-free decoder field tests: eight array types, independent fields, fanout, reuse, resize, empty and multiple entries per frame.
- Native GPU loopback: seven changing-length valid NetworkFrames, exact payload/diagnostic/message checks, object identity across shrink and zero/truncated payload rejection before payload readback. Existing 216 snapshot cases, lifecycle, CRC, TransSync and 30 ordering cases pass.
- Actual decoder/codec Udon bytecode with VRC GPU callbacks: the same seven payload-length cases and invalid lengths pass, plus existing snapshot, lifecycle, pool and ordering regressions.

No live VRChat client test or IL2CPP claim is made.

## Repeat

Use an isolated project referencing the modified Core and actual codecs. Copy `Editor/PayloadBufferValidation.cs` into `Assets/PayloadBuffers/Editor`. Set `TSMP_VALIDATION_RESULT` to an absolute result path. Run Unity with `-batchmode -quit -force-d3d11 -executeMethod PayloadBufferValidation.Measure` or `.Run`, plus `-projectPath` and `-logFile`.

For SDK validation also copy `PayloadBufferVmProbe.cs` into `Assets/PayloadBuffers`, then use `.RunUdon`. It creates its probe program asset and runs full client compilation before executing the helper bytecode.

The existing DecoderSnapshot runner covers GPU tests with `-Mode PlayLinear` and `-Mode Udon`. Field-array coverage uses `Validation~/ArrayCache` and `DecoderArrayCacheValidation.Run`.

Local evidence: `F:/Unity/TSMP/Validation-Results/issues14-17-18/payload-*` (measurement, native/helper/field results, Editor logs and GPU test subdirectories).

Profiler API: [SumAllSamplesInFrame](https://docs.unity3d.com/ja/2021.3/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html). The sample Count is an allocation event count; its Value is not used as allocated bytes.
