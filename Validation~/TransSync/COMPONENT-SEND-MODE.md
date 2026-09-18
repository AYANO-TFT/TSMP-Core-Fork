# Component Send Mode Validation

Recorded 2026-09-18 on `feat/component-send-mode`, based on `50c5a51`.

## Scope

`TSMPNetworkBehaviour.sendMode` is a serialized, sender-local override of automatic TransSync change filtering. Default (0) preserves field attributes, OnChange (1) enables filtering with refresh, and Always (2) includes unchanged values. Existing scenes default to 0. No packet, codec, binding array or RPC format changes are required.

Native Unity reads the source component directly. Udon captures a target once and reads its mode into a reused per-target array for that encode. SDK Editor encoding reads the proxy's enum rather than a stale backing heap value. Attribute metadata and committed snapshots/timestamps remain unchanged when switching modes. Minimum intervals, field eligibility, priority and capacity still apply.

The shared inspector displays one Send Mode dropdown below Network ID. Its serialized property supports multi-edit and Undo; specialized network inspectors use that same section. There is no separate setup workflow or required binding rebuild for changing the mode.

## Environment

- Unity 2022.3.22f1, Windows x64, NVIDIA GeForce RTX 4090, Direct3D11.
- Core: this worktree, `Packages/com.kibalab.tsmp.core`.
- Luma4: 0.0.4-beta.1 from the local codec repository.
- SDK validation: Worlds SDK 3.10.4-beta.2 and its bundled UdonSharp.
- Player: Development Mono, managed stripping disabled, Gamma color space.
- SDK-free project: `F:/Unity/TSMP/Validation-Results/issue22-20260913/After` with a local package reference to the actual Core worktree.
- SDK project: `F:/Unity/TSMP/Validation-Results/issue22-20260913/VRC`. Changed sources and the new enum/meta were copied into its embedded Core package; every Core C# file was hash-compared against the worktree after execution and matched. Generated Udon assets were kept in that isolated project.

## Results

Evidence root: `F:/Unity/TSMP/Validation-Results/component-send-mode/`. Each successful result file starts with `PASS`; adjacent `.log` files contain Unity output.

| Test | Result | Evidence relative to root |
| --- | --- | --- |
| SDK-free scheduler, live modes, intervals, refresh, independent components, multi-edit/Undo, metadata and manual Writer regression | Pass, 14 cases | `Native/20260918-193635-TransSync-result.txt` |
| SDK Editor scheduler and proxy enum lookup | Pass, 13 cases | `VRC/20260918-194204-TransSync-result.txt` |
| UdonSharp client compilation and actual Encoder VM execution | Pass | `VRC/20260918-193730-TransSyncVm-result.txt` |
| Native Editor Transform encoding through all modes without rebuilding bindings, Linear project | Pass | `Native/20260918-194318-EditorEncoding-result.txt` |
| SDK Editor proxy Transform encoding through all modes without rebuilding bindings | Pass | `VRC/20260918-194325-EditorEncoding-result.txt` |
| Gamma Play Mode GPU texture loopback | Pass, 11 decoded frames | `NativeGamma/20260918-194533-Play-result.txt` |
| Windows x64 Player build | Succeeded, 0 errors, 16 shader warnings | `NativeGamma/20260918-194610-Build.log` |
| Actual Gamma Player GPU texture loopback | Pass, 11 decoded frames | `NativeGamma/20260918-194708-Player-result.txt` |
| English, Korean and Japanese website | `npm run build` and `npm run typecheck` passed | Run in `Website~` |

The Udon VM exercises real compiled Encoder code, cross-behaviour field/event calls, unchanged filtering, mode switching, refresh, minimum intervals, failed outputs, priority, capacity deferral and RPC reservation. Compilation covered all programs in the SDK validation project, including the new probe (25 scripts). This is not an uploaded VRChat client test.

The native loopback additionally leaves one stationary Transform/paused Timeline state frame undecoded, selects Always, and verifies that two later frames restore deliberately disturbed receiver state through actual Luma4 encoding, GPU readback and decoding. Switching back to OnChange or Default suppresses unchanged output without rebuilding bindings. Existing animated Humanoid, Unicode variables, Timeline animation and RPC regression checks also pass.

Player artifact: `F:/Unity/TSMP/Validation-Results/issue22-20260913/After/Build/Mono/TSMPValidation.exe`. The adjacent `TSMPValidation.build-report.txt` records the build result, backend and stripping settings.

## Reproduction

Use `Validation~/Run-Validation.ps1` with `-UnityEditor`, `-Project`, `-Results` and these steps:

1. SDK-free: `TransSync`, `EditorEncoding`.
2. SDK present: `TransSyncVm`, `TransSync`, `EditorEncoding`.
3. SDK-free Gamma project: `Play`, `Build`, `Player`.
4. In `Website~`: `npm run build`, then `npm run typecheck`.

Full runner examples are in [the scheduling guide](./README.md). Use isolated projects and actual package references; refresh an embedded SDK test copy before running. Do not use `-nographics` for texture tests. Restore the validation project's original color space after the Gamma checks.

## Limitations and Failed Attempts

- An initial EditorEncoding run failed because the reused validation project contained two copies of `EditorPresentationValidation`. Only the old test copy was removed; the rerun passed.
- The full legacy loopback harness initially failed in the Linear project at `BlockExpansionCases.CheckFrame`: `Calibration symbols 0 and 1 are too close.` This occurs in the existing CPU raster-reader/block-expansion check before the new mode loopback checks. That Linear full-suite run is not a pass (`Native/20260918-194428-Play.log`). The Gamma rerun passed; native Editor mode tests separately passed in Linear. No rendering code was changed as part of Send Mode.
- The build's 16 warnings concern existing shaders (potentially uninitialized sample values and integer division), not C# compilation. Website commands also warn about the installed Node 21.2.0/npm 11.1.0 version pairing; both commands complete successfully.
- IL2CPP/stripping, Quest and a live VRChat world were not tested. No upload, push or release was performed.
