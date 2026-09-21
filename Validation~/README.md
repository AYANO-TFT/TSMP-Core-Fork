# Unity Support Validation

These scripts are test harnesses, not runtime package dependencies. Run them in dedicated projects only. The runner copies the harness into `Assets/Validation`; it does not copy or patch the TSMP implementation. Use local UPM dependencies pointing directly at the Core and Luma4 worktrees under test. For package-cache validation, use `npm pack` on those package directories, then reference both tarballs from a fresh project's manifest. Do not edit the installed cache.

## Projects

Create a Unity 2022.3.22f1 project without VRChat. Install the modified Core and Luma4 packages with Package Manager's Add package from disk. No custom Udon scripting defines should remain. Unity's standard 3D project modules are sufficient. The real Luma4 shaders and prefab are required.

For VRChat, create a separate Worlds project with the SDK, then reference the same worktree packages instead of published TSMP releases. Never copy SDK DLLs into the SDK-free project. Do not copy the ordinary Unity runtime test scripts into the VRChat project.

## Run

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe'
$runner = '.\Validation~\Run-Validation.ps1'
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Import
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Workflow
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Play
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step ArrayCache
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step EditorEncoding
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step NetworkEdges
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step RpcDelivery
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Build
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Player
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-NoSDK -Results F:\Unity\TSMP\Validation-Results -Step Inspect
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step InitializeSdk
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step Udon
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step UdonArrays
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step EditorEncoding
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step NetworkEdges
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step RpcDelivery
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step RpcQueueVm
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step Workflow
& $runner -UnityEditor $unity -Project F:\Unity\TSMP\Validation-VRC -Results F:\Unity\TSMP\Validation-Results -Step World
```

Run steps sequentially after import settles. `InitializeSdk` uses the SDK's `EnvConfig.SetActiveSDKDefines` to configure the isolated Worlds project; restart Unity before `Udon`. `World` uses `IVRCSdkWorldBuilderApi.Build`, including SDK validation and callbacks, and never calls an upload API. A first SDK build can change Player settings and request another Unity compile. Wait for that import and retry rather than bypassing validation.

`Build` creates a Development Windows x64 Mono Player with managed stripping disabled. `Player` deliberately does not use `-batchmode` or `-nographics`: an earlier Windows batch-mode Player did not complete GPU readback in the test timeout despite reporting a graphics adapter. The harness enables Run In Background. `Inspect` opens an EditorWindow and executes the six custom inspectors' IMGUI paths, then exits; it is not a screenshot-based layout review.

`ArrayCache` runs the decoder's payload parsing and field dispatch in the SDK-free Editor. It verifies independent arrays for different fields and for multiple recipients of the same variable. `UdonArrays` compiles all installed UdonSharp programs for the client, then executes the array-decoding helpers as real Udon bytecode in the Editor VM. Neither array step exercises a GPU; run `Play` separately for the existing texture loopback. Keep the two array steps in their respective SDK-free and SDK projects.

Both array steps cover byte, bool, int, float, Vector2, Vector3, Quaternion and Unicode string arrays: separate binding ownership, updates with unchanged lengths, resized arrays and empty arrays. Within a binding, same-length updates intentionally reuse the received array. Consumers needing historical snapshots must copy it. See `ArrayCache/RESULTS.md` for the recorded run.

`EditorEncoding` runs in either project with the shared Controller prefab and real Luma4 codec. It invokes the Controller's registered editor callbacks once to check that a due tick creates one frame, checks the written header CRC and payload size, then tests the frame-rate deadline, automatic/manual controls and callback registration through disable/enable/destroy. This is a deterministic editor scheduling test, not a wall-clock throughput benchmark. In ordinary Unity the native Encoder drives itself; only UdonSharp needs Setup's editor delegate. No user conversion or additional setup step is required. See `EditorEncoding/RESULTS.md` for the recorded run.

`NetworkEdges` runs component-level regression tests in either project: repeated blendshape packets after an external weight change, Renderer replacement, None/disabled/Continuous transitions, and malformed packets; Animator parameter/layer packing at and above the 255-entry limit, invalid selections, exact packet sizing, value round-trips, buffer reuse and Inspector selection limits. The SDK project executes the C# proxy path here, not the Udon VM. Run `Udon` separately for client compilation. See `NetworkEdges/RESULTS.md` for results and limitations.

`RpcDelivery` checks native RPC retention across retryable codec failures, exceptions and unusable frame capacity, then retries through the real Luma4 writer. Invalid arguments and oversized events are rejected at enqueue; events made unsendable by later argument/capacity changes are discarded without blocking later RPCs or variables. It checks diagnostics, supported types, exact-capacity boundaries, FIFO/repeat counts and capture-time/codec-write-time enqueue. Both projects test Decoder payload parsing and real GameObject Toggle dispatch across streams, repeated events, distinct key fields and legacy event IDs. The SDK variant of this step executes C# proxies, not Udon bytecode.

`RpcQueueVm` compiles all installed UdonSharp programs for the client, then executes the actual Encoder and a capture callback as separate Udon VMs in Play Mode. It verifies full 1/4-send budgets for capture-time RPCs, existing and newly queued events, failed frame output, FIFO order and manually written RPCs. Each test retrieves a fresh program heap. Client compilation is repeated after entering Play Mode because the SDK can automatically switch to editor bytecode on entry. Run `Play` separately for GPU readback and end-to-end RPC delivery. See `RpcDelivery/QUEUE-RESULTS.md` for issues #2/#4 and `RpcDelivery/RESULTS.md` for the earlier delivery run and finite-delivery limits.

## Assertions

- The same package Controller prefab in both environments, without a conversion menu or an explicit Apply Setup call in the workflow test.
- Deferred component, codec and binding preparation; independent generated resources; preservation of prefab links and settings through duplication, Undo/Redo, and scene reload.
- Source-prefab hash unchanged, no missing scene scripts, Udon backings present in the SDK environment.
- A codec hierarchy with multiple components preserves both cross-Udon and native-to-Udon serialized references when prepared.
- Real Luma4 encoding texture, header verification and asynchronous GPU readback.
- Six distinct Transform positions/rotations.
- AnimationClip sampled onto valid humanoid Animator avatars, arm rotation and hips position reception.
- Reflection-based int and Unicode string TransSync fields.
- Remote RPC, sender exclusion and duplicate retransmission suppression.
- Independent streams reusing an RPC event ID, with repeats still suppressed when switching back to the first stream.
- Blank texture rejection without applying new variable values. The expected header-mismatch error is explicitly allowed only during this negative test.
- Unity native custom inspectors: Setup, Encoder, Decoder, Transform, Humanoid and base NetworkBehaviour.
- All installed Udon programs compiled for the client, with nonempty bytecode checked for TSMP programs; shared Controller and backing Udon binding generation.
- SDK local Windows world bundle, separate from the standalone Player.

To put both endpoints in one test scene, the runtime harness maps sender network IDs to the receiver components after the ordinary Setup binding-generation check. Production usually places endpoints in separate scenes/projects; no decoder dispatch shortcut or direct assignment to received fields is used here.

IL2CPP, non-Windows platforms, other graphics APIs and an uploaded VRChat client session are not implied by these tests. See `WORKFLOW-RESULTS.md` for the shared-prefab run and log paths. `RESULTS.md` records the earlier SDK-isolation work; its manual controller-conversion workflow has been superseded and removed.
