# FFmpeg Streaming Regression Validation

S05 covers raw RGBA dimension mismatches. S06 covers publisher shutdown, restart and stale GPU/process callbacks. The tests reference the real Core package; they do not replace its publisher or writer implementation.

## Implementation

- Keep the public component fields, component GUID and FFmpeg argument format. Reject incompatible dimensions instead of rescaling TSMP pixels.
- Freeze dimensions and frame rate per session. Validate both the current source and the completed RGBA32 readback, including equal-byte-count shape changes.
- Give each session its own process, signal, writer, frame buffers and diagnostics. Compare the captured session identity before a GPU callback changes any publisher state.
- Keep background work independent of Unity component state. Copy diagnostics on the main thread. The pending frame and writer buffer have fixed sizes and cannot be replaced by a new session.
- Request cooperative stop and wake the writer. If it is blocked writing stdin, kill only that session's child process and wait again. Do not interrupt the thread or dispose its signal while it can still use it. If termination exceeds the bounded wait, report it and leave cleanup to that isolated writer.
- Stop on disable/destruction, reject disabled-component startup, and restore the run-in-background override on shutdown and failed startup.

## Run

Use dedicated Unity projects with the actual Core package referenced by a local `file:` dependency. Install FFmpeg and FFprobe with libx264 support. These Windows tests use PowerShell only to create a child process that deliberately never reads stdin. No live RTMP service, stream key or upload is required.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe'
$project = 'F:/Unity/TSMP/Validation-NoSDK'
$results = 'F:/Unity/TSMP/Validation-Results/s05-s06'
& './Validation~/Streaming/Run-Validation.ps1' -UnityEditor $unity -Project $project -Results $results -Step Editor
& './Validation~/Streaming/Run-Validation.ps1' -UnityEditor $unity -Project $project -Results $results -Step Build
& './Validation~/Streaming/Run-Validation.ps1' -UnityEditor $unity -Project $project -Results $results -Step Player
```

Use `-Ffmpeg` and `-Ffprobe` for executables outside PATH. Close the validation project's editor before each invocation. Run `Editor` again against a separate SDK project, then use the existing `Validation~/Run-Validation.ps1 -Step Udon` for full Udon client compilation. No publisher Udon program is expected: FFmpeg process launch remains native desktop functionality.

The runner copies only validation code to Assets/Validation/Streaming. Build creates a dedicated scene and a Windows x64 Development Mono Player with stripping disabled. Both Editor and Player execute the same 18 cases with graphics enabled. Real FFmpeg FLV output is decoded back to RGBA and inspected by FFprobe. RGBA row flipping is byte-exact before encoding; decoded grayscale values allow two levels of color-conversion rounding.

## Issue #5: Output Pacing

Input signals only update the pending frame. The writer uses a monotonic `Stopwatch` deadline for output slots, coalesces pending input to the latest frame, and skips missed slots after a blocked write. Idle repetition uses the same deadline. With repetition disabled, input pauses produce no output; rawvideo timestamps consequently pause too. This option does not preserve wall-clock media time during an input outage.

2026-09-13, same Unity/GPU/FFmpeg versions as below, SDK-free Editor and Windows Mono Player:

| Input at 30 FPS output setting | Before, Editor | After, Editor | After, Player |
| --- | --- | --- | --- |
| Slow input with repetition | 39.48 FPS | 30.05 FPS | 30.18 FPS |
| Fast input with repetition | 57.72 FPS | 30.03 FPS | 29.95 FPS |
| Bursts with repetition | 55.95 FPS | 30.15 FPS | 29.91 FPS |
| Fast input, then idle, no repetition | 54.73 FPS | 25.80 FPS | 25.71 FPS |

Counts are measured over approximately 2.1 seconds, including 320 ms of idle time. The first output slot is immediate, so short-window averages can slightly exceed 30 FPS. The no-repeat case intentionally produces fewer frames over its idle-inclusive measurement. All 18 cases passed in Editor and Player. FFmpeg `framemd5` output confirms one sequential timestamp per written frame at time base 1/30, no partial frames, and the final coalesced input's checksum. Empty input produces no frames and shutdown interrupts a one-second deadline. Windows x64 Mono build succeeded with zero errors and warnings.

Evidence root: `F:/Unity/TSMP/Validation-Results/issue5-20260913`.
- Before: `before/20260913-125923-Streaming-Editor.log` (four pacing failures).
- After Editor: `after/20260913-130355-Streaming-Editor.log`.
- Build: `after/20260913-130451-Streaming-Build.log`.
- Player: `after/20260913-130544-Streaming-Player.log`.
- Result text and frame timestamp/checksum files are retained alongside these logs.

## Results

2026-09-12: Unity 2022.3.22f1, Windows x64, RTX 4090 / Direct3D11, FFmpeg/FFprobe 2024-03-20-git-e04c638f5f with libx264. SDK project uses Worlds 3.10.4-beta.2 and bundled UdonSharp. Core is the working `fix/runtime-bugs` checkout, including its existing TransSync feature work.

Evidence root: `F:/Unity/TSMP/Validation-Results/s05-s06`.

| Check | Result | Log/result prefix |
| --- | --- | --- |
| Before correction | Both defects reproduced: mismatched dimensions accepted; old GPU callback submitted into new session | before/20260912-200709-Streaming |
| SDK-free Editor | 13 cases passed | final/20260912-202210-Streaming-Editor |
| Windows Mono build | Succeeded, zero errors and warnings | final/20260912-202256-Streaming-Build |
| Actual Windows Player | Same 13 cases passed | final-player/20260912-202343-Streaming-Player |
| SDK-present Editor | Same 13 cases passed | final-sdk/20260912-202411-Streaming-Editor |
| Full Udon client compile and Setup bindings | 13 TSMP programs valid; bindings passed | final-sdk/20260912-202500-Udon |

Player: `F:/Unity/TSMP/Validation-NoSDK/Build/Streaming/StreamingValidation.exe`. BuildReport summary is the adjacent `.build-report.txt`. The result directories retain FLV and decoded RGBA files for all four Texture2D/RenderTexture and flip combinations. Six stop/restart cycles confirm no surviving child processes or writer threads. The blocked-stdin test asserts termination within three seconds.

An earlier Udon invocation (`sdk/20260912-202024-Udon.log`) collided with an editor that had not exited and failed at Unity's project lock. It was rerun after exit; no code/compiler failure was involved. Expected invalid-input and process-failure cases emit warning logs. The validation-generated program reference changes are not part of the fix.

Not covered: a remote RTMP server, OBS/video-network delivery, IL2CPP/stripping, non-Windows process shutdown, or an uploaded VRChat client. Passing these tests does not make H.264 a lossless TSMP transport. No package version or release is changed by this work.
