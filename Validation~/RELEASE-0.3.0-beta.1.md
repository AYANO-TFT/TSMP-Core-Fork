# TSMP Core 0.3.0-beta.1 Validation

Date: 2026-09-12. Release scope: Timeline fixes (`6695f2d`), TransSync send scheduling (`d9c820b`), and FFmpeg dimension/session fixes (`da7f263`). The release preparation changes only versioning and documentation after these source commits.

## Environment

- Unity 2022.3.22f1 on Windows; NVIDIA RTX 4090, Direct3D11, graphics enabled.
- Dedicated projects: `F:/Unity/TSMP/Validation-NoSDK` and `F:/Unity/TSMP/Validation-VRC`.
- Both projects reference the real Core working checkout by local UPM dependency, not a modified package copy.
- Luma4 0.0.3; VRC project uses Worlds 3.10.4-beta.2 and bundled UdonSharp.
- Player target: Windows x64, Development Mono, managed stripping disabled.

## Release Gate

Evidence root: `F:/Unity/TSMP/Validation-Results/releases/core-v0.3.0-beta.1`. Each prefix below has a `.log` and, except Build, a `-result.txt` file.

| Check | Result | Evidence prefix |
| --- | --- | --- |
| Native TransSync | 11 cases passed | 20260912-203412-TransSync |
| Native Timeline | 19 cases passed | 20260912-203438-TimelineRegression |
| SDK-free Editor GPU loopback | 9 frames passed | 20260912-203456-Play |
| Windows Mono build | Succeeded, zero errors, one existing Luma4 shader warning | 20260912-203524-Build |
| Actual Windows Mono Player | 9 frames passed | 20260912-203758-Player |
| Real Udon VM TransSync | Scheduling, intervals, array changes, priority, RPC reservation, capacity deferral and output failure passed | 20260912-203814-TransSyncVm |
| Full UdonSharp client compilation | 13 TSMP programs valid, Setup bindings passed | 20260912-203903-Udon |

The loopback uses actual Luma4 shaders and GPU readback. It checks Transform position/rotation, animated Humanoid and Timeline, int/Unicode fields, repeated and independent-stream RPC delivery, and blank-source rejection without variable mutation.

Player output: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`. Its adjacent `TSMPValidation.build-report.txt` records the BuildReport result. The Player log is the release-gate `Player.log` above.

Run each step sequentially with the dedicated project's editor closed:

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
$results = 'F:/Unity/TSMP/Validation-Results/releases/core-v0.3.0-beta.1'
foreach ($step in @('TransSync', 'TimelineRegression', 'Play', 'Build', 'Player')) {
    & './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-NoSDK' -Results $results -Step $step
}
foreach ($step in @('TransSyncVm', 'Udon')) {
    & './Validation~/Run-Validation.ps1' -UnityEditor $unity -Project 'F:/Unity/TSMP/Validation-VRC' -Results $results -Step $step
}
```

Validation-generated Udon program cache references are excluded from release commits. The Encoder's updated field metadata is retained.

## Additional Evidence

The unchanged FFmpeg implementation passed thirteen cases in SDK-free and SDK-present Editors and a Windows Mono Player immediately before release preparation. Evidence, the reproduced pre-fix failures, executable paths and rerun commands are in [Streaming/README.md](Streaming/README.md). FFmpeg/FFprobe version: 2024-03-20-git-e04c638f5f with libx264. Tests include real decoded FLV output, both texture source types, vertical flip, six restart cycles and blocked-stdin shutdown.

Earlier feature-specific native and Udon results are in [TransSync/README.md](TransSync/README.md). English, Korean and Japanese documentation built before release preparation; `npm run typecheck` passed again during preparation. GitHub Actions separately rebuilds the documentation and release archives on publication.

## Limits

One existing `SampleBlockLuma` shader warning remains in the Core loopback build. This validation does not cover IL2CPP/stripping, Quest, non-Windows FFmpeg shutdown, a remote RTMP service, an SDK world build, or an uploaded VRChat client. Udon compilation and VM execution are reported separately from a world upload. Archive generation and VPM registration are distribution checks, not runtime test substitutes.
