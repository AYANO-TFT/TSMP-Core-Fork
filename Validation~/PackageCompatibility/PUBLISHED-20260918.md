# Published Codec Package Validation

Recorded 2026-09-18, after Core 0.3.0-beta.2 was published. These checks use public release downloads and public VPM installation, not mutable development checkouts.

## Releases

| Package | Version | Release commit | Distribution |
| --- | --- | --- | --- |
| Core | 0.3.0-beta.2 | 141ae02 | GitHub and VPM |
| Luma4 | 0.0.4-beta.1 | b74939a | GitHub and VPM |
| RGB16 | 0.0.3-beta.3 | 7817f03 | GitHub and VPM |
| RGB20 | 0.0.3-beta.4 | 8dd8df4 | GitHub and VPM |
| Color256 | 0.0.3-beta.3 | 876ec85 | GitHub and VPM |

Each codec was committed and pushed on main, then tagged from a version-specific release branch. All four GitHub releases are prereleases and include a ZIP, UnityPackage and package.json. The existing stable Luma4 0.0.3 remains Latest. No tag was rewritten.

All codecs declare UPM Core `0.3.0-beta.2` and VPM Core `>=0.3.0-beta.2`. SDK dependencies remain VPM-only. Packet layout and codec IDs are unchanged.

## Environment

- Unity 2022.3.22f1, Windows x64, RTX 4090, Direct3D11, Gamma project color space.
- SDK-free projects use local UPM references to folders extracted from the downloaded public ZIPs.
- The SDK project started with Base/Worlds 3.10.4-beta.2 and bundled UdonSharp, without TSMP packages. VPM CLI 0.1.28 installed the exact TSMP versions from the configured public repository.
- Player: Windows x64 Development Mono, managed stripping disabled.
- Evidence root: `F:/Unity/TSMP/Validation-Results/codec-releases-20260918/published`.

## Results

| Check | Result | Evidence relative to root |
| --- | --- | --- |
| Public GitHub downloads and VPM SHA-256 | Passed, all five packages | `inventory.json`, `archives/` |
| Released ZIP contents vs. Git tags | Passed, byte-for-byte; Core 386, Luma4 39, RGB16 53, RGB20 38, Color256 48 files | `archives/expected-*.zip` and downloaded ZIPs |
| SDK-free Core + Luma4 | Passed | `Minimum-Luma4/result.txt`, `Editor.log` |
| SDK-free Core + Luma4 + RGB16 | Passed | `Minimum-RGB16/result.txt`, `Editor.log` |
| SDK-free Core + Luma4 + RGB20 | Passed | `Minimum-RGB20/result.txt`, `Editor.log` |
| SDK-free Core + Luma4 + Color256 | Passed | `Minimum-Color256/result.txt`, `Editor.log` |
| All-codec actual Decoder in Play Mode | Passed, 27 frames | `results/upm/play.txt`, `play-editor.log` |
| Windows Player build | Succeeded, zero errors, 16 existing shader warnings | `results/player/Player/Calibration.build-report.txt` |
| Built Player execution | Passed, 27 Decoder frames | `results/player/player.txt`, `Player.log` |
| Public VPM installation of exact versions | Passed; all installed files matched the public ZIPs before Udon validation | `vpm-install-exact.log`, `VPM-VRC/Packages/vpm-manifest.json` |
| Full UdonSharp client compilation | Passed, 23 scripts in the project | `results/vpm/udon-editor.log` |
| Actual compiled codec Udon VM execution | Passed for all four codecs | `results/vpm/udon.txt` |

Minimum tests assert material, shader and prefab references, Luma4 header bytes, payloads of 4/56/1027 bytes, sample sizes 1/4 and missing preparation-material fallback. All-codec Decoder tests assert header CRC and raw payload bytes through GPU readback, with codec switching and preparation policy checks. The Udon VM checks Float32 allocation, preparation passes, byte parity, fallback and cleanup. These are not transport or full avatar synchronization tests.

Compact result files and the public inventory are retained in [Evidence-Published](Evidence-Published). The actual Player is `results/player/Player/Calibration.exe` under the evidence root.

## Reproduce

Use the same prepared SDK-free and SDK template projects as the candidate workflow. `Stage-Published.ps1` refuses an existing destination, downloads the five exact versions, verifies their manifests and VPM hashes, and creates independent projects. It does not edit working package checkouts.

```powershell
$root = 'F:/Unity/TSMP/Validation-Results/codec-release-repeat'
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
./Validation~/PackageCompatibility/Stage-Published.ps1 -NoSdkTemplate 'F:/Unity/TSMP/Validation-Codecs-NoSDK' -VrcTemplate 'F:/Unity/TSMP/Validation-Codecs-VRC' -OutputRoot $root
foreach ($package in (Get-Content "$root/inventory.json" -Raw | ConvertFrom-Json)) {
    vpm add package "$($package.name)@$($package.version)" -p "$root/VPM-VRC"
}
./Validation~/PackageCompatibility/Run-Minimum.ps1 -UnityPath $unity -StagingRoot $root
./Validation~/GpuCalibration/Run-Validation.ps1 -UnityPath $unity -ProjectPath "$root/UPM-NoSDK" -ResultsDirectory "$root/results/upm" -Mode Play
./Validation~/GpuCalibration/Run-Validation.ps1 -UnityPath $unity -ProjectPath "$root/UPM-NoSDK" -ResultsDirectory "$root/results/player" -Mode Player
./Validation~/Run-Validation.ps1 -UnityEditor $unity -Project "$root/VPM-VRC" -Results "$root/results/vpm-import" -Step Import
./Validation~/GpuCalibration/Run-Validation.ps1 -UnityPath $unity -ProjectPath "$root/VPM-VRC" -ResultsDirectory "$root/results/vpm" -Mode Udon
```

Check installed package versions and hashes before opening Unity; do not treat a zero CLI exit code as proof of installation. Explicit version selection is documented by the [VPM CLI](https://vcc.docs.vrchat.com/vpm/cli/).

## Initial Attempts and Limits

- `vpm resolve project` returned zero without installing the uninstalled TSMP dependencies. Unversioned `vpm add package` then selected the legacy 1.0.0 entries for RGB16, RGB20 and Color256. Those runs are not counted as successful beta validation. Explicit `package@version` installation selected the intended betas; manifests, locks and every installed file were checked against the public ZIPs before the final Udon run. VCC users should select these beta versions explicitly rather than relying on automatic latest selection.
- Unity validation can modify copied material parameters and generated Udon assets. The archives and working release repositories remain unchanged. Final VPM comparison used archive bytes directly rather than package folders already exercised by native tests.
- The four isolated minimum configurations were SDK-free. The SDK/Udon checks used all four codecs together, not four isolated SDK projects. The package dependency issues remain open for that remaining isolated SDK matrix; public full-set compilation does not replace it.
- Live VRChat client execution, Quest, IL2CPP/stripping, other GPUs/APIs and streamed video capture are not verified here. No world was uploaded. Core's separately recorded Linear loopback limitation is not claimed fixed by these Gamma codec tests.
- Publication does not change existing RGB16 low-bit/quantization limitations. Performance comparisons are the earlier recorded GPU microbenchmarks, not new whole-client performance claims.
