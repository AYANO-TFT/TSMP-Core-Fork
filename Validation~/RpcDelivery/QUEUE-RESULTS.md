# RPC Queue Regression Results

Run date: 2026-09-13. Issues: [#2](https://github.com/kibalab/TSMP-Core/issues/2) and [#4](https://github.com/kibalab/TSMP-Core/issues/4). Baseline: `fe4011abd8ebdaa21c967191540f590d4d2bee17` on `main`. The fixes were tested as local working-tree changes; no release was performed.

## Changes

- #2: the Udon Encoder records whether it wrote a pending RPC before capturing variables. Only a successful output containing that queued event consumes a repeat. A callback that appends the first event after the RPC-writing phase no longer loses its first send. A manually written RPC does not consume a different queued event.
- #4: ordinary Unity validates lower-level RPC and TransRPC messages before enqueue, using the existing wire writer and a reusable validation buffer. Unsupported/null argument values, excessive argument counts and messages above the protocol or known output capacity are rejected with `false` and `lastError`.
- At frame construction, an event made unsendable by argument mutation or capacity reduction is discarded with its Network ID, method hash and failure reason. Later RPCs and variables can still be written. Discard diagnostics use the Encoder's existing warning budget/settings and remain in `lastError` even when the remaining frame succeeds.
- Missing output/codec, capacity below the network header size and codec output failures do not consume otherwise valid queued events. The queue remains FIFO, one queued event per frame, with the existing finite repeat budget. There is no ACK, guaranteed delivery or extra retry after the budget is exhausted.
- Packet layout, codec IDs, Decoder dispatch/deduplication and component GUIDs are unchanged. Validation-generated Core program references were restored to their original content.
- English, Korean and Japanese Encoder API pages describe the rejection/discard policy and correct the existing return-type examples from `void` to `bool`.

## Environment

- Unity 2022.3.22f1, Windows x64, NVIDIA GeForce RTX 4090, Direct3D11.
- Native project: `F:/Unity/TSMP/Validation-NoSDK`, no VRCSDK/UdonSharp.
- Udon project: `F:/Unity/TSMP/Validation-VRC`, Worlds 3.10.4-beta.2 and bundled UdonSharp.
- Modified Core package: `F:/Unity/TSMP/TSMP-Core-main/Packages/com.kibalab.tsmp.core`.
- Real Luma4 0.0.3 package: `F:/Unity/TSMP/TSMPCodec-Luma4-UnitySupport/Packages/com.kibalab.tsmp.codec.luma4`.
- Baseline comparisons referenced a detached worktree of the baseline commit. Both validation manifests were then restored to the modified Core worktree for final tests. Only test harnesses were copied into the validation projects.

## Results

All log paths below are relative to `F:/Unity/TSMP/Validation-Results/rpc-queue-20260913/`. Each execution log has an adjacent `-result.txt`, except the build log.

| Check | Result | Log |
| --- | --- | --- |
| Baseline ordinary Unity | Reproduced four failing cases: invalid enqueue, one-byte capacity overflow, capacity reduction blocking the queue, and mutated invalid arguments blocking the queue | `before-native/20260913-124127-RpcDelivery.log` |
| Baseline Udon client VM | Reproduced three failing cases: removal before first transmission at repeat count 1, early budget consumption at count 4, and an unrelated manual RPC consuming a queued event | `before-udon/20260913-124155-RpcQueueVm.log` |
| Modified ordinary Unity | 12 cases passed, including real Luma4 writes, actual warning-log capture, FIFO, serialization boundaries, codec failure/exception retention and Decoder Toggle dispatch | `native-final/20260913-124435-RpcDelivery.log` |
| Full UdonSharp client compilation and VM | Compilation of all 25 installed programs passed. All nine queue cases passed: repeat counts 1/4, existing/late-enqueued events, successful/failed output and manual RPC isolation | `udon-final/20260913-124323-RpcQueueVm.log` |
| Native Play Mode GPU loopback | Passed, nine frames: Transform, Unicode/int fields, humanoid pose, Timeline and RPC application with repeat suppression | `loopback/20260913-124455-Play.log` |
| Windows x64 Mono Development Player build | Succeeded, zero errors, one existing Luma4 shader warning about potentially uninitialized `SampleBlockLuma`; managed stripping disabled | `loopback/20260913-124512-Build.log` |
| Built Player execution | Passed the same nine-frame GPU loopback, including RPC application and blank-input rejection | `loopback/20260913-124634-Player.log` |

Player: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`.

Build report: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.build-report.txt`.

The first new VM harness run reused the program asset's mutable heap and therefore incorrectly assumed that each case started at event ID 1. The harness now retrieves an independent serialized program instance for every VM. It also explicitly recompiles client bytecode after entering Play Mode, because SDK Play Mode preparation can replace it with editor bytecode. Both baseline and final comparisons use this corrected harness.

## Reproduction

Use `Validation~/Run-Validation.ps1` with the Unity executable and dedicated project paths above. Run these steps sequentially:

1. `RpcDelivery` in the SDK-free project.
2. `RpcQueueVm` in the SDK project.
3. `Play`, `Build`, then `Player` in the SDK-free project.

The runner prints the log and result paths. `RpcQueueVm` invokes the real compiled Encoder and capture callback through separate Udon VMs, parses the emitted payload to count actual event IDs and validates header CRC/length. Its failed-output cases create a frame that exceeds codec capacity after the queue-writing phase; native tests separately inject codec false returns and exceptions. The end-to-end loopback steps use shaders and GPU readback without `-nographics`.

## Limits

- Udon VM execution in the Unity Editor is not an uploaded VRChat client test. No live world upload, OBS/Spout/MediaMTX delivery test, IL2CPP build or additional GPU/backend test was performed.
- The typed low-level RPC API exists only in the ordinary Unity Encoder. This change does not add typed arguments or a new oversized-TransRPC policy to Udon.
- The Player test is the existing end-to-end loopback; the queue fault-injection matrix runs in the Editor/native and Udon VM harnesses.
- Every repeated frame can still be lost. This is the accepted finite-send policy, not a remaining delivery-guarantee defect.
- Whole-frame coherence, application atomicity, ordering and the deduplication-window/session questions remain outside these fixes.
