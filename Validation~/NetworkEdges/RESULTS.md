# Blendshape and Animator Regression Validation

Run date: 2026-09-11. Scope: review findings R13 and R15. Existing uncommitted R11 changes were preserved; no commit or release was performed for this run.

## Changes

- Blendshape Discrete reception compares the received weight with the actual Renderer weight rather than a cached last-applied value. Receiving 50, externally resetting to 0, then receiving 50 again restores 50. The obsolete cache and its Continuous bookkeeping were removed.
- Animator capture collects valid parameter and layer indices into reusable bounded arrays. Buffer sizing, header counts and writing all use those same selections. Each section accepts at most 255 entries, in selection order. Invalid entries do not consume that quota. Layer indices must also fit in the existing one-byte field.
- The Animator Inspector limits All and individual selection to the same packet capacities. Existing selected entries can still be unchecked at the limit, and repeated selection does not add duplicates.
- Packet versions, layouts, value precision, public serialized configuration fields and interpolation algorithms are unchanged. Related private Udon field metadata was regenerated while retaining existing program asset references.

The runtime tolerates duplicate configured entries by bounding their number, rather than adding a per-frame deduplication scan. This preserves ordering and avoids introducing quadratic Udon work. Inspector selection normally produces unique entries. The parameter-name hash cache's same-length invalidation issue from R05 is separate and unchanged.

The blendshape fix restores the value on receipt; it does not change Animator execution order or continuously enforce Discrete values between packets.

## Environment

- Core release worktree based on `e09d154`, with R11 and these R13/R15 changes applied.
- Unity 2022.3.22f1 on Windows.
- SDK-free project: `F:\Unity\TSMP\Validation-NoSDK`.
- SDK project: `F:\Unity\TSMP\Validation-VRC`, VRChat Worlds `3.10.4-beta.2` and bundled UdonSharp.
- Real local Core and Luma4 package worktrees; Luma4 `0.0.3-beta.3`.
- GPU regression: NVIDIA GeForce RTX 4090, Direct3D11.

## Results

| Validation | Result |
| --- | --- |
| Native before fix | Reproduced blendshape weight remaining 0 after repeated 50; IndexOutOfRangeException for 256 selected parameters, 256 selected layers, and oversized mixed selections |
| Native after fix | PASS: all eight component and Inspector cases |
| SDK C# proxy after fix | PASS: the same eight cases |
| UdonSharp client compile | PASS: all installed programs compiled; 13 TSMP programs with nonempty bytecode; backing bindings present |
| Existing GPU Play loopback | PASS: six frames; Transform, animated humanoid pose, int/Unicode fields, RPC repeat suppression and blank-input rejection |

The component tests cover:

- Blendshape selected-key filtering, repeated values, external weight changes, same-size Renderer replacement, None, disabled/re-enabled components, truncated packets, Continuous settling and subsequent Discrete reception.
- Animator 254, 255 and 256 entries, 300-entry mixed selections containing invalid entries, null/empty and unequal name/type array lengths.
- Exact agreement between packet length, entry counts and final cursor; same-size packet reuse; bool/int/float round-trip through the actual component packet reader.
- Inspector All Parameters capacity and individual parameter/layer limits, removal at capacity, duplicate selection and rejection of layer index 256.

Boundary fixtures deliberately repeat real Animator parameters and layers to isolate serialization limits from the size of a particular controller. The tests do not claim a real 256-layer Animator rig was exercised. The Inspector UI's visual layout was not screenshot-tested.

These are component-level tests, not a new blendshape/Animator-through-video test. The separate existing GPU loopback covers the common transport. No uploaded VRChat session, Udon VM execution of these two components, new Player build or SDK world build was run.

## Reproduction and Logs

Run `Validation~/Run-Validation.ps1 -Step NetworkEdges` in each dedicated project using the README command format. Run `-Step Udon` in the SDK project and `-Step Play` in the SDK-free project for the additional regressions.

Evidence root: `F:\Unity\TSMP\Validation-Results\network-edges-20260911`. Each stem has `.log` and `-result.txt` files:

- Before fix: `before/20260911-092135-NetworkEdges`.
- Final native: `after-native/20260911-092540-NetworkEdges`.
- Final SDK proxy: `after-vrc/20260911-092557-NetworkEdges`.
- Client compile: `regression/20260911-092642-Udon`.
- GPU loopback: `regression/20260911-092748-Play`.

Unrelated generated Udon asset references and sample resource changes were discarded after validation.
