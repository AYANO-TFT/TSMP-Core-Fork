# G01 Adaptive Calibration LUT

Validated on 2026-09-13 with Unity 2022.3.22f1, D3D11 and an NVIDIA RTX 4090.
The Udon project used VRChat SDK 3.10.4-beta.2 and its bundled UdonSharp.
Core and all four codec projects used their actual working packages, not
modified copies inside validation projects.

## Enabled Paths

| Codec/mode | LUT condition |
| --- | --- |
| Luma4 | Effective sample size greater than one |
| RGB16 direct, fixed 4/4/4 or variable bits | Effective sample size greater than one |
| RGB16 Refine, fixed or variable | Never; original shader retained |
| RGB20 | Effective sample size greater than one |
| Color256 Robust Refine | Any supported sample size, including one |
| Other Color256 modes | Original shader retained |

Empty output and missing preparation resources use the original decode path.
The LUT is one-row, linear ARGBFloat, point-filtered and refreshed from the
current source before every byte pass. No Half/8-bit quantization, cross-frame
reuse or extra readback was introduced. The Core hook contains no named
optional-codec branches. One-line Korean comments explain the codec policies.

## Measurements

GPU microseconds, four-byte payload, including preparation where enabled:

| Mode, sample size | Original | Adaptive |
| --- | ---: | ---: |
| Luma4, 4 | 68.960 | 26.880 |
| RGB16 fixed direct, 4 | 29.856 | 11.256 |
| RGB16 variable 5/6/4 direct, 4 | 38.072 | 23.192 |
| RGB20, 4 | 158.976 | 15.392 |
| Color256 Robust Refine, 4 | 806.424 | 378.872 |
| Color256 Robust Refine, 1 | 84.856 | 57.728 |

The expanded experiment also tried RGB16 Refine. Fixed 4/4/4 at sample four
went from 59.808 to 85.024 us; variable 5/6/4 went from 45.184 to 87.744 us.
Both were excluded, and their shader sources restored exactly. Earlier G01
verification had already shown regression/extra overhead with single-sample
Luma4, RGB16 and RGB20, so those retain the original path.

Each marker contains 32 repeated passes, with a synchronization boundary
between A/B submissions. Fifteen valid GPU samples are collected after ten
warmup frames. Separate wall timings include submission/readback overhead,
but neither timing isolates the real VRChat VM cost of per-pass setup.
Sample sizes two and three and payload sizes 56 and 1024 also showed gains
for enabled modes. These are measurements on one unlocked desktop GPU,
not a guarantee for other GPUs or VRChat client frame rates.

## Verification

- GPU comparisons: 336 cases across seven shader modes. Original, disabled
  and enabled paths produced identical bytes, including exact/noisy/flat
  inputs, both orientations, automatic/1/2/3/4/8 samples and partial output.
- Additional checks passed for effective sampling at block sizes 1/2/4/8/16,
  empty-output fallback, missing material fallback, Float32 allocation,
  resizing, reuse, distinct codec instances, shared material ownership,
  material switching and disable/re-enable/destruction.
- Full Udon client compilation passed. Actual codec VM calls exercised
  PrepareDecode, RenderTexture creation, VRCGraphics prepass blits, byte
  parity and cleanup for all four codecs.
- Native Editor decoder loopback passed 27 frames, including header CRC,
  exact payload bytes and automatic path selection across codec/sample changes.
- Windows x64 Mono Player build succeeded with zero errors and 16 shader
  compiler warnings. The warnings concern loop/initialization analysis in
  the existing sampling helpers and integer division; prepass shaders also
  use those helpers. No shader algorithm outside G01 was changed to suppress
  these warnings.
- The built Player executed successfully on D3D11 and passed the same
  27-frame decoder checks. Both local shader variants survived the build.
  Managed stripping was disabled; IL2CPP and Android were not tested.
- An initial Player harness run reported stale diagnostics. The final
  harness waits for component initialization, explicitly checks readback
  completion and uses a bounded 30-second wait. This work does not claim to
  fix decoder frame-lifecycle behavior based on that initial harness result.
- No live VRChat world upload/client run was performed. Udon VM verification
  is reported separately from the native Player run.

## Evidence

Raw measurements and final summaries are in [Measurements](Measurements).
Repeatable commands are in [README](README.md).

Local full logs and binaries:

- `F:/Unity/TSMP/Validation-Results/g01-implementation/final/gpu-editor.log`
- `F:/Unity/TSMP/Validation-Results/g01-implementation/final/udon-editor.log`
- `F:/Unity/TSMP/Validation-Results/g01-implementation/final/player-editor.log`
- `F:/Unity/TSMP/Validation-Results/g01-implementation/final/Player.log`
- `F:/Unity/TSMP/Validation-Results/g01-implementation/final/Player/Calibration.exe`

## Compatibility

Packet layout, codec IDs, existing asset GUIDs and legacy shader paths are
unchanged. Existing custom codecs do not have to override PrepareDecode.
Codec instances without the new preparation material remain functional via
the old decode path; updated package prefabs assign the material automatically.

The changed codec sources require the new Core preparation API. Update these
source checkouts together. No package version or release tag was changed in
this work. Before publishing, release the new Core API first and raise the
codec manifests' minimum Core version to that release.
