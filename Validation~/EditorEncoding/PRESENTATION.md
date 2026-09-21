# Editor output presentation (#18)

Validated on 2026-09-13 using Unity 2022.3.22f1, D3D11, RTX 4090 and SDK-free / SDK 3.10.4-beta.2 projects.

## Change

Remove `SetupApplier.BlitEncoderOutputInEditor` and its calls from Setup's automatic editor update and manual Encode Now. Native encoding already presents its staging texture; the SDK-present editor path already presents its full-size or block-symbol texture through the appropriate expansion material. Setup must not copy it again with an ordinary Blit, especially after failed or skipped encoding.

No new settings, serialized fields, wire changes or conversion steps are required. This does not change the automatic encoding ownership fixed earlier; it removes redundant output presentation.

## Measurement and pixel checks

`Editor/EditorPresentationValidation.cs` records the actual `Graphics.Blit` profiler marker on the calling thread only around an encode. Expected-image rendering and CPU pixel readback happen outside that recording scope. These are Blit counts, not GPU duration estimates.

- Before, SDK editor manual Setup encode: **2 Blits**, regression failure.
- Before, SDK-free normal encoding: **1 Blit**, as its hidden `outputTexture` is normally null. A retained legacy texture reference makes a skipped manual encode perform an unwanted copy; the regression reproduces that failure.
- After, both editor configurations: **1 Blit** per successful encoding and **0** for skipped/failed encoding. Output pixels remain untouched on skipped/failed frames.
- Twenty success cases per configuration: Luma4, RGB16, RGB20, Color256, back to Luma4; manual/automatic drivers and the block-symbol toggle. The native path continues to use its own full-resolution staging texture; the SDK path exercises both full-size and block expansion.
- Real 643x363 GPU output is pixel-identical to the encoder-owned presentation. The SDK block path also explicitly checks that the non-block-aligned right border remains black.

Material copies isolate tests from package assets. Changed runtime source hashes in the SDK validation copy match the working package exactly.

## Regression verification

- Native Play Mode and actual Udon decoder/codec bytecode with VRC GPU readback pass the expanded snapshot, payload-length and ordering tests. Full client UdonSharp compilation passed during #14/#17 verification; Setup is outside Udon compilation, and both final editor configurations compile and pass presentation tests after its removal.
- Final Windows x64 Development **Mono**, stripping disabled, Player build: **Succeeded, 0 errors, 16 shader warnings**. The graphics-enabled executable passes 216 changing-input cases, changing-length NetworkFrames, cancellation/lifecycle, CRC, native TransSync application and 30 frame-window cases.
- No IL2CPP, live VRChat client or SDK world build/upload was performed. The Player result does not stand in for those tests.

## Reproduction and evidence

Copy `Editor/EditorPresentationValidation.cs` into an isolated project's Editor folder and `Validation~/DecoderSnapshot/DecoderSnapshotValidation.cs` into a runtime folder. Install the actual Core and all four codec packages. Set `TSMP_VALIDATION_RESULT` to an absolute result path, then run Unity with:

```text
-batchmode -quit -force-d3d11 -projectPath <project> -executeMethod EditorPresentationValidation.Run -logFile <log>
```

Local evidence root: `F:/Unity/TSMP/Validation-Results/issues14-17-18`.

- `presentation-before-{native,udon}.{txt,log}` and `presentation-final-{After,VRC}.{txt,log}`.
- `final-player/Player/Snapshot.build-report.txt`, `final-player/Player-editor.log`, `final-player/Player.log`, `final-player/Player.txt`.
- Executable: `final-player/Player/Snapshot.exe`.

Repeat Player verification with `Validation~/DecoderSnapshot/Run-Validation.ps1 -Mode Player` and the isolated SDK-free project/results directory arguments.
