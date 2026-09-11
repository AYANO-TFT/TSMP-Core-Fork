# Editor Encoding Ownership Validation

Run date: 2026-09-11. Scope: review finding R11, duplicate automatic encoding in the SDK-free Editor.

## Change

Before the fix, both the native Encoder and Setup registered automatic editor callbacks with independent frame-rate timers. Invoking the Controller's registered callbacks once produced two frames. Setup now registers its delegate and declares its editor timer only when UdonSharp is available. The native Encoder continues to drive itself, including without a Setup component.

Automatic setup preparation, manual encoding, runtime Update methods, component settings and the wire format are unchanged. No conversion menu or additional user setup is required. In ordinary Unity, `autoEncode` and `frameRate` on the Encoder control automatic encoding; `driveEncoderInEditor` continues to control Setup's UdonSharp editor delegate.

## Environment

- Core release worktree based on `e09d154` (the separately committed R02 array fix), with this R11 change applied.
- Unity 2022.3.22f1, Windows.
- Native project: `F:\Unity\TSMP\Validation-NoSDK`, without VRCSDK/UdonSharp.
- SDK project: `F:\Unity\TSMP\Validation-VRC`, VRChat Worlds `3.10.4-beta.2` with bundled UdonSharp.
- Actual Core and Luma4 worktree packages; Luma4 `0.0.3-beta.3`.
- GPU regression: NVIDIA GeForce RTX 4090, Direct3D11.

## Results

| Test | Result |
| --- | --- |
| Native, before fix | Expected FAIL: one callback pass advanced `frameIndex` by 2 |
| Native, after fix | PASS: one callback pass advances `frameIndex` by 1 |
| UdonSharp editor delegate | PASS: one callback pass advances `frameIndex` by 1 |
| Both editor configurations | PASS: valid header CRC and nonempty payload; frame-rate deadline; Auto Encode disabled; direct and Setup manual encoding; callback removal and re-registration through disable/enable/destroy |
| Native without Setup | PASS: automatic encoding remains active |
| UdonSharp client compile | PASS: all installed programs compiled, 13 TSMP programs with nonempty bytecode, bindings present |
| Native Play Mode GPU loopback | PASS: six frames, Transform, animated humanoid pose, int/Unicode fields, RPC repeat suppression and blank-input rejection |

The first Udon editor test checked diagnostic counters after `clearAfterEncode` had reset them and failed despite generating one frame. The test now validates the written header and payload size instead. No runtime change was made for that test assertion.

The scheduling test invokes only the registered Controller callbacks in a deterministic pass and controls their deadlines through reflection. It is not a throughput benchmark or a measured CPU speedup. The Udon editor test invokes the existing C# proxy path, as normal editor preview does; the separate client compile is not an uploaded VRChat runtime test. No Player build or SDK world build was repeated for this editor-only fix.

## Reproduce and Evidence

Use `Validation~/Run-Validation.ps1 -Step EditorEncoding` in each dedicated project. The README contains the complete command format. Run `-Step Udon` in the SDK project and `-Step Play` in the SDK-free project for the additional regressions.

Logs are under `F:\Unity\TSMP\Validation-Results\editor-encoding-20260911`. Each stem has `.log` and `-result.txt` files:

- Before fix: `before/20260911-091130-EditorEncoding`.
- Final native test: `after-native/20260911-091418-EditorEncoding`.
- Final Udon editor test: `after-vrc/20260911-091348-EditorEncoding`.
- Udon client compile: `regression/20260911-091445-Udon`.
- GPU loopback: `regression/20260911-091537-Play`.

Compilation-generated program asset reference changes were discarded. This change does not require new serialized fields or asset GUIDs.
