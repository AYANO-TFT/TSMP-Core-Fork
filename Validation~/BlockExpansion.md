# Block Expansion Regression (#12)

`BlockExpansionCases` is part of the existing SDK-free Play/Build/Player loopback runner. It invokes the real block-texture writer, expansion shader and GPU readback, comparing every output pixel against a full-resolution raster blit. It also verifies header CRC and every payload byte at maximum Luma4 capacity. The output is first filled white to detect uncleared remainder pixels.

The expansion uses the configured integer block size from the top-left, matching the full-resolution writer and decoder. Right/bottom remainder pixels are black. Output dimensions and block size are refreshed on each blit; block counts are no longer stretched over the output resolution. The helper `EncoderUdonTextureRuntime.BlitEncodedTexture` now requires `blockSize` as its last argument.

## Results

2026-09-13, Unity 2022.3.22f1, RTX 4090 / D3D11, real Luma4 0.0.3:

- Before: 641x361, block size 8 failed at pixel (0, 0).
- After: 640x360/8, 641x361/8, 1920x1080/16 and 1920x1088/16 passed in both Linear and sRGB render textures.
- All eight cases passed in Editor Play Mode and Windows x64 Development Mono Player (stripping disabled).
- The nine-frame Transform/Humanoid/Timeline/Unicode/RPC GPU loopback also passed in both environments.
- Player build succeeded with zero errors and one pre-existing Luma4 shader warning about `SampleBlockLuma`. Other graphics APIs and IL2CPP were not tested.

Evidence root: `F:/Unity/TSMP/Validation-Results/issue12`.

- Reproduction: `before/20260913-135018-Play.log`.
- Fixed Play Mode: `after/20260913-135119-Play.log`.
- Build: `player/20260913-135212-Build.log`.
- Player: `player/20260913-135257-Player.log`.
- Executable: `F:/Unity/TSMP/Validation-NoSDK/Build/Mono/TSMPValidation.exe`.

Run with `Validation~/Run-Validation.ps1 -Step Play`, then `Build`, then `Player`, using the dedicated SDK-free project described in the main validation README.
