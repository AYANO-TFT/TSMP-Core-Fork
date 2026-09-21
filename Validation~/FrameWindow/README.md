# Frame window validation (#24)

Date: 2026-09-13. Branch: `fix/frame-order-window`, based on `ac8b067` (decoder snapshot fix).

## Policy

`TSMPDecoder.frameWindowSize` defaults to 256 frames and has an effective minimum of 1. With `skipDuplicateFrames` enabled, the current stream accepts forward UInt32 sequence differences below half the range. Equal indices are duplicates. Otherwise frame zero is also admitted when the previously applied index is at least the window size. Other candidates are skipped without payload dispatch. A different stream bypasses comparison. The baseline advances only after successful application.

No session ID, datagram field or version is added. This is a heuristic: missing zero, delayed old zero, and old high indices arriving after a reset cannot be reliably distinguished from a restart/new data. The window is not a replay cache or an allocated image buffer. RPC deduplication is unchanged.

## Tests and reproduction

The shared harness under `../DecoderSnapshot/` now also runs 30 table-driven frame-window cases through the real decoder. Columns in `FrameOrderCases()` are previous index, incoming index, window, expected acceptance, previous stream ID (incoming stream is zero), filtering disabled, and whether a previous frame exists.

Coverage includes default initialization to 256 in C# and Udon, 255/256/257 at default window, a custom 512 window, minimum/zero settings, UInt32 wrap, exact half-range ambiguity, changed streams, disabled filtering, no prior state, and separate skip counters. Native tests dispatch an actual Int32 variable and also verify that an invalid reset payload does not advance the baseline, followed by `256 -> 0 -> 1 -> 2 -> 1`. Udon tests execute the compiled decoder with actual VRCGraphics/readback and valid empty network messages.

From the repository root, using isolated projects configured as described in the snapshot harness:

```powershell
& './Validation~/DecoderSnapshot/Run-Validation.ps1' -ProjectPath 'F:/Validation/NoSDK' -ResultsDirectory 'F:/Results/Window/PlayLinear' -Mode PlayLinear
& './Validation~/DecoderSnapshot/Run-Validation.ps1' -ProjectPath 'F:/Validation/NoSDK' -ResultsDirectory 'F:/Results/Window/Player' -Mode Player
& './Validation~/DecoderSnapshot/Run-Validation.ps1' -ProjectPath 'F:/Validation/VRC' -ResultsDirectory 'F:/Results/Window/Udon' -Mode Udon
```

## Recorded results

Unity 2022.3.22f1, RTX 4090, D3D11. SDK 3.10.4-beta.2 with bundled UdonSharp. Core uses the working package; SDK validation uses an isolated copy with the updated decoder and editor source. Codec revisions are the same as the snapshot validation.

| Check | Result |
| --- | --- |
| SDK-free Linear Editor | PASS: 30 ordering cases, variable application, failed reset and restart sequence |
| Windows x64 Mono Development Player | Build succeeded (0 errors, 16 existing shader warnings); execution PASS with real graphics-enabled batch readback |
| Full UdonSharp client-program compilation | PASS |
| Compiled Udon Editor VM | PASS: all 30 ordering cases |
| Snapshot regressions | PASS: 216 native cases, 16 Udon frame cases and 8 Udon cancellation cases |
| Documentation | PASS: English/Korean/Japanese production build and TypeScript check |
| Actual VRChat client / world build / IL2CPP / Android | Not run |

See compact results under `Evidence/`. Final logs are under `F:/Unity/TSMP/Validation-Results/issue24-window/final/`, with `PlayLinear`, `Player`, and `Udon` subdirectories. The executable is `Player/Player/Snapshot.exe`; the build summary is `Player/Player/Snapshot.build-report.txt`. These tests do not certify delivery, replay protection, or minimized normal-window behavior.
