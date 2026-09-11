# RPC Delivery Regression Results

Run date: 2026-09-11. Base commit: `e09d154` on `release`, including the previously uncommitted Editor encoding and network component fixes. No version bump, commit, upload or release was performed for this change.

## Changes

- R08: `EncoderNativeFrameBuilder.BuildNetworkPayload` no longer consumes the queued RPC. The native Encoder advances it only after a successful codec write and output blit, and only if that payload included an RPC. Payload/capacity/codec failures preserve the head event and its repeat count. Queue diagnostics are refreshed after output. The Udon Encoder already commits after output and is unchanged.
- R09: Decoder deduplication now uses `(streamId, networkId, rpcHash, eventId)`. Changing streams does not clear other streams' entries, so returning to an already seen stream still suppresses its repeats while cached.
- Wire format, queue capacity, repeat count settings, FIFO policy, dedup capacity and legacy events without positive IDs are unchanged. Decoder Udon field metadata includes the additional stream-ID cache; validation-project-specific compiled program references were not retained.
- Native helper callers that use `BuildNetworkPayload` directly must now call `AdvanceQueuedRpcs` after successfully outputting a payload containing an RPC. The standard Encoder handles this automatically.

## Environment

- Unity 2022.3.22f1, Windows x64, NVIDIA GeForce RTX 4090, D3D11.
- Core `0.2.0-beta.1` worktree: `F:/Unity/TSMP/TSMP-Core-release/Packages/com.kibalab.tsmp.core`.
- Luma4 `0.0.3-beta.3`: `F:/Unity/TSMP/TSMPCodec-Luma4-UnitySupport/Packages/com.kibalab.tsmp.codec.luma4`.
- Native project: `F:/Unity/TSMP/Validation-NoSDK`, no VRCSDK/UdonSharp.
- SDK project: `F:/Unity/TSMP/Validation-VRC`, Worlds 3.10.4-beta.2 with its bundled UdonSharp.
- Both projects reference the actual package worktrees through local UPM dependencies. The test runner copies only validation harnesses.

## Results

All log paths below are relative to `F:/Unity/TSMP/Validation-Results/rpc-delivery-20260911/`. Each test result shares the log stem with a `-result.txt` suffix, except the build report.

| Step | Result | Evidence |
| --- | --- | --- |
| Native regression before fix | Reproduced R08 and R09 | `before/20260911-093953-RpcDelivery.log`: codec failure/exception removed the pending event, capacity failure and intermittent failures consumed repeats, Stream 2's first event was incorrectly suppressed. |
| Native regression after fix | 7 cases passed | `native/20260911-094051-RpcDelivery.log`: failure and exception retention followed by successful real Luma4 writes; capacity and serialization retry; two FIFO events with three successful repeats each and intervening failures; late enqueue during variable-only write; cross-stream Toggle dispatch; key dimensions, legacy IDs and bounded eviction. |
| SDK C# proxy regression | 2 Decoder cases passed | `vrc/20260911-094113-RpcDelivery.log`: actual Toggle target dispatch across streams and repeat suppression, plus key/legacy checks. Native Encoder tests are explicitly excluded here. |
| Full UdonSharp client compilation | Passed | `vrc/20260911-094144-Udon.log`: 13 TSMP programs have nonempty bytecode; Setup backing bindings validated. |
| Native Play Mode GPU loopback | Passed, 9 frames | `native/20260911-094249-Play.log`: real Luma4 output, header decoding and asynchronous GPU readback; Transform, humanoid pose, int/Unicode fields and RPC repeat checks. Additional stream A/B/A frames produce RPC counts 2/3/3, confirming independent events and repeat suppression using the decoded header. |
| Windows x64 Mono Player build | Succeeded, 0 errors, 1 warning | `native/20260911-094321-Build.log`. Warning: potentially uninitialized `SampleBlockLuma` in the Luma4 decode shader; shaders are unchanged by this fix. Development build, managed stripping disabled. |
| Built Player execution | Passed, 9 frames | `native/20260911-094409-Player.log`: the same real GPU loopback, stream A/B/A and blank-input rejection checks. |

Player executable: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`.

Build report: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.build-report.txt`.

## Reproduction

Use `Validation~/Run-Validation.ps1` with Unity 2022.3.22f1 and the dedicated projects above. Run `RpcDelivery` in each project, `Udon` in the SDK project, then `Play`, `Build` and `Player` in the native project. The README lists the command syntax. No graphics-disabled runtime test was used.

## Limits

- These fixes prevent local queue loss and cross-stream false duplicates. They do not add acknowledgements. Dropping every repeated frame can still lose an event; a successful local blit is not a receiver acknowledgement.
- The dedup cache still holds 32 distinct events. Events evicted from it can execute again if an old frame reappears. Event IDs without a positive value retain legacy no-dedup behavior.
- Reusing the same Stream ID and event IDs after sender restart is still ambiguous. Use distinct Stream IDs for independent senders; no inferred restart/session protocol was added.
- SDK Decoder behavior was tested through C# proxies; client Udon bytecode was compiled, not executed in an uploaded VRChat world during this run. OBS, Spout, MediaMTX, packet loss on a real network and IL2CPP were not tested here.
- Unrelated review items, including whole-frame validation before applying messages and binding-cache invalidation, are outside this change.
