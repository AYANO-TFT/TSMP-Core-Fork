# Codec Preparation API Package Compatibility

Tracks Core #28 and the dependent Luma4 #2, RGB16 #1, RGB20 #1 and Color256 #1 issues. Documentation implementation is tracked in Core #21.

These are **unpublished candidates**. Updating a manifest and validating a Git archive does not publish a package or verify a public repository installation. Keep the release issues open until the final gate below is complete.

Recorded checks, known warnings and remaining validation are in [Results](RESULTS.md).

## Candidate Set

| Package | Candidate | Package source commit |
| --- | --- | --- |
| Core | 0.3.0-beta.2 | ce55d15 |
| Luma4 | 0.0.4-beta.1 | f7f3d82 |
| RGB16 | 0.0.3-beta.3 | eb69fe5 |
| RGB20 | 0.0.3-beta.4 | 3373c73 |
| Color256 | 0.0.3-beta.3 | 97b6e00 |

Each codec declares Core `0.3.0-beta.2` in UPM and `>=0.3.0-beta.2` in VPM. The prior Core 0.2.0 and 0.3.0-beta.1 releases lack `PrepareDecode` and the calibration helpers. A runtime shader fallback cannot resolve a missing C# base API.

UPM package manifests use version strings. For local/disk or Git installation, supply Core explicitly through the **project** manifest as well; no registry or GitHub discovery is implied by a package's numeric dependency. VPM uses its configured repositories and version ranges. SDK dependencies remain in `vpmDependencies`, not UPM dependencies.

Luma4 is needed for frame headers even when the payload uses an optional codec. The minimum operational combination for an optional codec is Core + Luma4 + that codec, not that codec alone. No packet or codec-ID changes are introduced by this compatibility work.

## Reproduce Candidate Validation

Use dedicated Unity 2022.3.22f1 projects as ProjectSettings templates, with their editors closed. The SDK-free template must have no Udon symbols. The VRC template must contain, or use absolute local file dependencies for, the actual Base/Worlds SDK packages. No Unity or SDK stubs are generated.

`Stage-Candidates.ps1` requires committed package contents, refuses an existing destination, archives each package from Git, verifies manifests and records source commits/ZIP SHA-256 hashes. No validation project points to a mutable working package checkout.

```powershell
$root = 'F:/Unity/TSMP/Validation-Results/issue28-20260913-candidates'
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
./Validation~/PackageCompatibility/Stage-Candidates.ps1 -RepositoriesRoot 'F:/Unity/TSMP' -NoSdkTemplate 'F:/Unity/TSMP/Validation-Codecs-NoSDK' -VrcTemplate 'F:/Unity/TSMP/Validation-Codecs-VRC' -OutputRoot $root
./Validation~/PackageCompatibility/Install-EmbeddedCandidates.ps1 -StagingRoot $root
vpm resolve project "$root/VPM-VRC"
./Validation~/GpuCalibration/Run-Validation.ps1 -UnityPath $unity -ProjectPath "$root/UPM-NoSDK" -ResultsDirectory "$root/results/upm" -Mode Play
./Validation~/GpuCalibration/Run-Validation.ps1 -UnityPath $unity -ProjectPath "$root/UPM-NoSDK" -ResultsDirectory "$root/results/player" -Mode Player
./Validation~/GpuCalibration/Run-Validation.ps1 -UnityPath $unity -ProjectPath "$root/VPM-VRC" -ResultsDirectory "$root/results/vpm-final" -Mode Udon
./Validation~/PackageCompatibility/Run-Minimum.ps1 -UnityPath $unity -StagingRoot $root -ProjectPrefix Minimum-Final
./Validation~/PackageCompatibility/Test-Manifests.ps1 -StagingRoot $root -SemanticVersioningAssembly '<installed-vpm-cli>/SemanticVersioning.dll'
```

Let the SDK finish first-import C# compilation before invoking the explicit Udon compile. The first VRC run here generated SDK utility scripts, then the harness invoked Udon compilation too early; it reported missing C# classes for those utility scripts. Reopening the unchanged project and repeating the Udon check passed. This initial failed run is retained in `results/vpm`, not hidden as a success.

The installed VPM CLI 0.1.28 did not support the documented `vpm add package <local-folder>` call: it logged `Could not get match` while returning exit code zero. No package was installed by that command. `Install-EmbeddedCandidates.ps1` instead extracts the recorded ZIPs into the dedicated project's Packages folder and records an explicit VPM manifest. Resolver completion, installed manifests and the actual VPM SemanticVersioning library are checked separately. This is **embedded candidate validation**, not evidence of a download through VCC or the public VPM repository.

`Run-Minimum.ps1` creates a new SDK-free project per codec and compiles only Core, Luma4 and the selected optional codec. It checks prefab/material/shader references, 4/56/1027-byte payloads, sample sizes 1/4, missing preparation fallback, and Luma4 header bytes through real GPU readback. These isolated tests use default codec options. The full-set calibration suite additionally checks the codec variants through the actual Decoder and Udon VM.

The initial minimum-test harness had an ambiguous `PackageInfo` type reference. It was fixed by qualifying the Unity PackageManager type; the package sources were not changed. `Minimum-Luma4/Editor.log` retains that harness failure, and `Minimum-Final-*` records the corrected runs.

## Release Gate

1. Confirm the candidate versions with the maintainer. Remove candidate-only/unreleased wording when preparing the actual release; do not describe it as published early.
2. Commit and push on main. Bring the reviewed main commits into release, then create the matching Core tag there. Do not develop directly on release or rewrite existing tags.
3. Publish Core first and verify its GitHub release assets, package version and VPM registration. Download those assets and record their hashes.
4. Bring each codec's reviewed main into its release branch and publish the corresponding candidate. Do not publish a codec against an unavailable Core version.
5. Install the **downloaded public packages** in fresh SDK-free UPM and VPM/SDK projects. Enable pre-release visibility for beta selection; confirm resolved versions rather than trusting exit codes alone.
6. Repeat the minimum combinations, all-codec Decoder loopback, full UdonSharp compile/VM checks and Player checks against those installed packages. Confirm shader/material/prefab references, not just ZIP creation.
7. Record public installation results and close the related issues only after these checks pass. Do not close Core #28 solely because metadata has been committed.

## Scope Limits

The candidate checks use Unity 2022.3.22f1, D3D11, RTX 4090, VRChat Worlds/Base 3.10.4-beta.2 with bundled UdonSharp, and a Windows x64 Development Mono Player with managed stripping disabled. No live VRChat session, world upload, IL2CPP, Android or additional GPU/API is covered. Those remain separate validation work (#20). GPU pass checks do not prove transport reliability or frame snapshot/transaction behavior.

Official format references: [Unity package manifest](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-manifestPkg.html), [VPM packages and version ranges](https://vcc.docs.vrchat.com/vpm/packages/).
