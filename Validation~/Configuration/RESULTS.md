# Configuration and allocation regression validation

Validated on 2026-09-11 against the working tree based on release commit `c8bd95a`.

## Environment

- Core: `F:/Unity/TSMP/TSMP-Core-release/Packages/com.kibalab.tsmp.core`, version unchanged at `0.2.0-beta.1`.
- Unity: 2022.3.22f1, Windows x64, RTX 4090, Direct3D11.
- Native project: `F:/Unity/TSMP/Validation-NoSDK`, no VRCSDK/UdonSharp.
- SDK project: `F:/Unity/TSMP/Validation-VRC`, Worlds 3.10.4-beta.2 with bundled UdonSharp.
- Real codec: Luma4 0.0.3-beta.3, referenced from `TSMPCodec-Luma4-UnitySupport`.
- Player: Mono, managed stripping disabled. Both validation projects reference the actual package worktrees.

## Results

| Check | Result | Evidence under `F:/Unity/TSMP/Validation-Results/configuration-20260911/` |
| --- | --- | --- |
| Native configuration regressions | PASS, eight test groups | `native/20260911-171431-Configuration-result.txt` |
| SDK configuration regressions and full Udon client compilation | PASS | `vrc/20260911-171621-Configuration-result.txt` |
| Udon VM codec query and binding lookup | PASS, actual compiled client bytecode | Same Configuration result and matching `.log` |
| Udon VM Timeline commands on a real PlayableDirector in Play Mode | PASS | `vrc/20260911-171352-TimelineVm-result.txt` |
| Native Editor GPU loopback | PASS, nine frames | `native/20260911-171506-Play-result.txt` |
| Windows x64 Mono Player build | Succeeded, zero errors, one shader warning | `native/20260911-171522-Build.log` |
| Built Player GPU loopback | PASS, nine frames | `native/20260911-171557-Player-result.txt` |
| Documentation build | PASS, English/Korean/Japanese | `npm run build` in `Website~` |

Matching `.log` files accompany the Unity result files. The executable is `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`; its sibling `TSMPValidation.build-report.txt` records the build result. The unchanged shader warning concerns `SampleBlockLuma` potentially being uninitialized. npm also warned about the installed Node 21/npm 11 pairing; the three locale builds succeeded without dependency or lockfile changes.

## Covered changes

- R06: reserve pre-existing nonzero Network IDs before allocating new IDs or resolving duplicates; verify repeated rebuilds and components sharing an object.
- R05: change binding targets, IDs, hashes and field names without changing lengths; preserve old recipients' array ownership; reuse unchanged caches. Rename Animator parameters in place. Replace humanoid Animator/Avatar, edit bone selections in place, remove the Avatar, enable finger expansion and clear the selection. Change codec options without replacing the codec, including the compact Udon query response.
- S02: add, reorder, remove and duplicate codec sources, rename a configured instance and change its parent; retained instances keep their options and unchanged arrays are reused.
- S01: native Timeline state is read from the Director, not time deltas. Verify first capture at time zero, repeated captures at the same time, paused seeking, and Play/Pause/Resume/Stop. Execute the four commands through actual Udon bytecode in Play Mode.
- R10: retain a 278-byte payload buffer through twenty initialization/header cycles and allocate the explicitly requested size before manual-layout readback. The parser still uses exact payload length; changing actual payload size can still allocate.
- Existing GPU loopback covers Transform, AnimationClip-driven humanoid bones/root, Component int/Unicode fields, repeated RPCs across streams, and blank-input rejection.

## Limits and compatibility

- Udon exposes neither `PlayableDirector.state` nor `playableGraph`. API discovery is saved in `F:/Unity/TSMP/Validation-Results/configuration-20260911-api.txt`. Native Unity observes external playback controls; Udon requires commands through the sync component. Automatic completion or direct external Director control cannot be observed reliably in Udon; explicitly call Stop/Pause at the end of a sequence. No time-delta fallback claims to infer those states.
- An initial Timeline VM attempt in Edit Mode hit the SDK's Play-Mode-only object security initialization. The separate `TimelineVm` step runs in Play Mode and reloads the compiled program after domain reload. No SDK security code was modified.
- No VRChat upload, live client session, SDK world build or IL2CPP run was performed for this change.
- Protocol bytes, codec IDs, frame duplication/coherence policy and interpolation algorithms were not changed. No package versions, releases or commits were created.
- Keep the normal Unity/UdonSharp recompilation after source updates. Validation-only serialized program GUID changes and dependent Luma4 generated artifacts were removed; updated Core field metadata retains its original asset references.

## Reproduce

Use the existing `Run-Validation.ps1` runner with `-UnityEditor`, `-Project`, `-Results` and `-Step`.

- Native project: `Configuration`, `Play`, `Build`, `Player`.
- SDK project: `Configuration`, then `TimelineVm`. Configuration compiles all Udon programs with `IsEditorBuild=false` before running its VM checks.
- API discovery: after copying the Configuration harness with the runner, invoke `TimelineApiValidation.Run` in the SDK project's Unity batch mode with `TSMP_VALIDATION_RESULT` set.
- Documentation: `npm ci --no-audit --no-fund`, then `npm run build` in `Website~`.
