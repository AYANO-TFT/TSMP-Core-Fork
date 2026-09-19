# Published performance beta verification

Recorded 2026-09-19. This verifies the public release artifacts after the resource optimizations. It does not introduce new performance measurements; the release-note numbers come from [the implementation report](../OptimizationFeasibility/IMPLEMENTED.md).

## Versions and publication

| Package | Version | Release commit |
| --- | --- | --- |
| Core | 0.3.0-beta.3 | 589e8dd |
| Luma4 | 0.0.4-beta.2 | 0ccd896 |
| RGB16 | 0.0.3-beta.4 | 877c04c |
| RGB20 | 0.0.3-beta.5 | 42bd89e |
| Color256 | 0.0.3-beta.4 | d12d60d |

Every repository was committed and pushed on main before tagging. Core main was merged into release. Codec tags were made from version-specific release branches created from main. No existing tag was changed. All five GitHub releases are prereleases; stable Core 0.2.0 and Luma4 0.0.3 remain Latest.

All codecs declare Core 0.3.0-beta.3 in UPM and >=0.3.0-beta.3 in VPM. SDK requirements remain VPM-only. Luma4's buffered/GPU writer APIs require the new Core; update Core first. Select the exact prerelease versions in VCC rather than relying on automatic latest selection.

## Distribution checks

- All five release workflows completed successfully and produced ZIP, UnityPackage and package.json assets.
- Downloaded public ZIPs match their GitHub SHA-256 digests and the public VPM index at https://vpm.kiba.red/index.json.
- All 578 package files match the corresponding release tag byte-for-byte: Core 400, Luma4 39, RGB16 53, RGB20 38, Color256 48. Comparison uses `git -c core.autocrlf=false archive` so Windows checkout conversion does not change the expected bytes.
- Downloaded UnityPackages match their GitHub SHA-256 digests. UnityPackage import is not claimed by this check.
- GitHub release bodies match the committed English release notes, including measurement context and limitations.
- The website TypeScript check, English/Korean/Japanese builds and GitHub Pages deployment succeeded. Installation pages in all three locales returned HTTP 200.

## Public package runtime checks

Separate SDK-free and SDK projects were created from validation project settings, without copying Library caches or generated programs. Each references its own extraction of the public ZIPs through local UPM file dependencies. Runtime tests therefore do not reference the development package checkouts. VPM index/hash verification was performed, but VCC installation was not repeated in this run.

Environment: Unity 2022.3.22f1, Windows, RTX 4090, D3D11, Gamma. Native Player: Windows x64 Development Mono, managed stripping disabled. SDK: Worlds 3.10.4-beta.2 with bundled UdonSharp.

| Check | Result | Evidence |
| --- | --- | --- |
| SDK-free fresh import and actual Player build | Passed; zero errors, 16 existing shader warnings | player/build-report.txt |
| Built native Player execution | Passed | player/status.txt |
| Native prediction/fallback and two-slot regression | Passed | player/regression.txt, player/overlap-regression.txt |
| Full client-target UdonSharp compile after SDK initialization | Passed, 24 scripts; confirmed on another launch | udon-confirm/compile.txt |
| Compiled SDK VM prediction/fallback and two-slot regression | Passed on both follow-up launches | udon-reopen/, udon-confirm/ |
| Automatic encoder/decoder material preparation into backing UdonBehaviour | Passed | udon-confirm/preparation.txt |

Native and compiled Udon regression cover all four codecs, changing payload sizes, multi-row output, source overwrite, sample changes, current-header CRC rejection, CPU/GPU encoder switching, disable/re-enable, concurrent decoders, bounded slots and ordered completion. Twelve RPCs with deliberately skipped repeat copies and eight overlapping RPCs each executed exactly once in their respective tests.

The public Player is `F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.3/results/player/Player/ContinuousFrames.exe`. Full import, build and runtime logs are under that release verification root. Compact evidence and the archive inventory are in [Evidence-Published-20260919](Evidence-Published-20260919).

## Reproduce

Download the five ZIPs listed in the inventory, verify hashes, extract separately for native and SDK testing, and point fresh validation project manifests at those extracted folders. Use the existing validation harness from main:

```powershell
./Validation~/ContinuousFrames/Run-Validation.ps1 -ProjectPath F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.3/NoSDK -ResultsDirectory F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.3/results/player -Mode Player -Filter 'prediction-regression,overlap-regression'
./Validation~/ContinuousFrames/Run-Validation.ps1 -ProjectPath F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.3/VRC -ResultsDirectory F:/Unity/TSMP/Validation-Results/release-0.3.0-beta.3/results/udon-confirm -Mode Udon -Filter 'prediction-regression,overlap-regression'
```

The Udon command explicitly performs full client-target compilation before executing the compiled programs in the SDK Editor VM with real GPU rendering/readback. Unity may update generated assets in the extracted validation packages; the downloaded archives and release repositories remain unchanged.

## SDK initialization attempts

The first SDK-project launch failed validation because freshly generated SDK UtilityScripts (including GlobalToggleObject and BoneFollower) did not yet resolve to C# classes. Its log is retained under results/udon/Editor.log; it is not counted as a successful fresh-import compile.

Reopening Unity allowed full compilation of 24 scripts and passed the VM regression. During that launch's earlier automatic SDK upgrade, Unity also logged a serialized-program cache creation failure for the validation-only ContinuousFrameProbe. The subsequent explicit client-target compile and runtime tests passed. Those results are retained under results/udon-reopen/.

A further launch, with the same public package source and no SDK/product C# modifications, again compiled all 24 scripts and passed the entire VM regression. Its results are under results/udon-confirm/. The earlier class/cache errors did not recur. This confirms operation after initialization, not an error-free first SDK bootstrap. Initial diagnostics are retained in the compact evidence rather than silently dropping unsuccessful attempts.

## Limits

This is local package and runtime regression validation, not a new FPS/CPU/GPU benchmark or a transport-loss test. No live VRChat client, SDK world bundle, populated avatar scene, OBS/Spout/MediaMTX transport, Quest, IL2CPP/stripping, other graphics API or long-duration soak was run. Existing shader warnings remain. The earlier performance report and 30-FPS delivery report retain their own workload and latency limitations.
