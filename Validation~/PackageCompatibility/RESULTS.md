# Preparation API Candidate Results

For the later 2026-09-18 publication and public package installation checks, see [Published Results](PUBLISHED-20260918.md). This file retains the original candidate-only results.

Date: 2026-09-13. These results cover **local release candidates**, not public VPM/GitHub downloads. No tag, push or release was performed for this work.

Source versions/commits and reproduction commands are in [README](README.md). All package files tested were extracted from recorded Git archives. The ordinary Unity projects use those extracted folders as local UPM dependencies. The SDK project embeds the ZIP contents; it does not reference the main working checkouts. Udon-generated program assets remain inside the isolated SDK project.

## Environment

- Unity 2022.3.22f1, Windows, Direct3D11, NVIDIA GeForce RTX 4090; graphics enabled, never `-nographics`.
- VRChat Base/Worlds 3.10.4-beta.2 and bundled UdonSharp in the separate VPM-format project.
- VPM CLI 0.1.28 and its actual `SemanticVersioning.dll`, with pre-release matching enabled for beta dependencies.
- Windows x64 Development Player, Mono, managed stripping disabled.
- Docusaurus 3.10.1; Node 21.2.0/npm 11.1.0. Build/typecheck passed, but npm warns that this Node version is outside its supported range. No dependency upgrade was performed to suppress the warning.

## Checks

| Check | Result | Evidence under the root below |
| --- | --- | --- |
| Recorded package ZIPs, versions and source commits | Pass | `inventory.json`, `archives/` |
| VPM ranges reject Core 0.2.0 and 0.3.0-beta.1 | Pass for all four codecs | `manifest-results.txt` |
| VPM ranges accept the candidate and subsequent beta/stable 0.3.0 values | Pass; version-range test, not runtime compatibility of future releases | `manifest-results.txt` |
| SDK-free UPM resolution | Pass; all five packages resolve to candidate folders, no VRChat package | `UPM-NoSDK/Packages/packages-lock.json` |
| Core + Luma4 minimum | Pass | `Minimum-Final-Luma4/result.txt`, `Editor.log` |
| Core + Luma4 + RGB16 minimum | Pass | `Minimum-Final-RGB16/result.txt`, `Editor.log` |
| Core + Luma4 + RGB20 minimum | Pass | `Minimum-Final-RGB20/result.txt`, `Editor.log` |
| Core + Luma4 + Color256 minimum | Pass | `Minimum-Final-Color256/result.txt`, `Editor.log` |
| Actual Decoder, all codecs, native Editor | Pass, 27 frames | `results/upm/play.txt`, `play-editor.log` |
| Full client UdonSharp compilation | Pass after SDK first-import C# refresh | `results/vpm-final/udon.txt`, `udon-editor.log` |
| Real Udon VM codec preparation | Pass for all four codecs, allocation, shader parity, fallback and cleanup | `results/vpm-final/udon.txt` |
| Windows Player build | Succeeded; 0 errors, 16 shader warnings | `results/player/Player/Calibration.build-report.txt`, `results/player/player-editor.log` |
| Built Player execution | Pass, 27 Decoder frames with LUT policy and exact header/payload bytes | `results/player/player.txt`, `results/player/Player.log` |
| Documentation | English/Korean/Japanese builds and TypeScript check passed; all three preparation API anchors exist | `Website~/build`, local npm output |
| Public repository installation | Not run: candidates have not been published | Release gate remains open |

The minimum checks use actual prefab/material/shader assets, 4/56/1027-byte payloads, sample sizes 1/4 and missing preparation material fallback. They exclude unrelated optional codecs. Full-set Decoder checks additionally switch codec variants and validate header CRC, frame index and payload bytes. They use payload-readback-only mode and do not replace separate variable/RPC application tests.

The 16 Player shader warnings are the existing sampling-helper/loop-analysis and integer-division warnings also recorded in [GPU calibration validation](../GpuCalibration/RESULTS.md). No shader source was changed by this dependency/documentation work.

## Evidence Location

`F:/Unity/TSMP/Validation-Results/issue28-20260913-candidates`

Compact result files and archive provenance are retained in [Evidence](Evidence).

Player executable: `results/player/Player/Calibration.exe` under that root.

Initial failed attempts are retained: the local-folder VPM CLI command did not install a package despite returning zero, the first SDK compilation ran before generated utility scripts had C# classes, and the initial minimum harness had an ambiguous PackageInfo type. These are described in the reproduction README. Final successful checks do not overwrite those logs.

## Remaining Release Work

Core #28 and the codec dependency issues remain open until the matching Core is published first, dependent codecs follow, and downloaded public package combinations pass fresh UPM/VPM installation and runtime checks. Core #21's documentation change is committed separately.

No live VRChat client, world upload, IL2CPP/stripping, Android or additional GPU/API was tested. The SDK project contains all codecs for full Udon compilation; isolated per-codec projects here are SDK-free. Public per-codec SDK installation remains part of the release gate.
