using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using K13A.TSMP;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public static class StreamingValidation
{
    private static readonly List<string> Results = new List<string>();
    private static string directory;
    private static bool failed;

    public static void Run()
    {
#if UNITY_EDITOR
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
#endif
        directory = Path.GetDirectoryName(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"));
        Test("Reject source/output dimension mismatch before starting FFmpeg", DimensionMismatch);
        Test("Ignore GPU callbacks from the previous publishing session", StaleReadback);
        Test("Discard old callbacks without clearing a newer in-flight request", NewReadbackState);
        Test("Reject a source resize including equal-byte-count shape changes", SourceResize);
        Test("Reject an incompatible completed GPU readback before row flipping", ReadbackMismatch);
        Test("Validate dimensions and YUV420p before launching", InvalidDimensions);
        Test("Write Texture2D/RenderTexture RGBA frames and vertical flip through real FFmpeg", FrameOutput);
        Test("Repeat the last frame without reallocating or mixing buffers", RepeatFrame);
        Test("Stop and restart repeatedly without stale process diagnostics", Restart);
        Test("Detect a failed FFmpeg process even while edit-mode capture is paused", FailedProcess);
        Test("Failed startup and repeated stop restore background execution", FailedStartup);
        Test("Disable with a pending GPU request leaves no writer or callback state", Disable);
        Test("A child that never reads stdin cannot hang shutdown", BlockedWriter);
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"),
            (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
        if (failed) throw new InvalidOperationException("Streaming regression failed");
    }

    private static void Test(string name, Action action)
    {
        try { action(); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static TSMPFfmpegRtmpPublisher Create(string name)
    {
        var publisher = new GameObject(name).AddComponent<TSMPFfmpegRtmpPublisher>();
        publisher.sourceTexture = new Texture2D(16, 8, TextureFormat.RGBA32, false);
        publisher.checkRtmpTcpBeforeStart = false;
        publisher.ffmpegPath = Environment.GetEnvironmentVariable("TSMP_VALIDATION_FFMPEG") ?? "ffmpeg";
        publisher.rtmpUrl = Path.Combine(directory, name + ".flv");
        publisher.extraArguments = "-y";
        publisher.repeatLastFrameWhenIdle = false;
        return publisher;
    }

    private static void Destroy(TSMPFfmpegRtmpPublisher publisher)
    {
        publisher.StopPublishing();
        AsyncGPUReadback.WaitAllRequests();
        UnityEngine.Object.DestroyImmediate(publisher.sourceTexture);
        UnityEngine.Object.DestroyImmediate(publisher.gameObject);
    }

    private static void Capture(TSMPFfmpegRtmpPublisher publisher)
    {
        typeof(TSMPFfmpegRtmpPublisher).GetMethod("RequestFrame", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(publisher, null);
    }

    private static void DimensionMismatch()
    {
        var publisher = Create("mismatch");
        try
        {
            publisher.useSourceDimensions = false;
            publisher.width = 8;
            publisher.height = 8;
            publisher.StartPublishing();
            Check(!publisher.isPublishing, "Mismatched dimensions were accepted");
            Check(!string.IsNullOrEmpty(publisher.lastError), "No mismatch diagnostic");
        }
        finally { Destroy(publisher); }
    }

    private static void StaleReadback()
    {
        var publisher = Create("stale-old");
        try
        {
            publisher.StartPublishing();
            Check(publisher.isPublishing, publisher.lastError);
            Capture(publisher);
            publisher.StopPublishing();
            publisher.rtmpUrl = Path.Combine(directory, "stale-new.flv");
            publisher.StartPublishing();
            Check(publisher.isPublishing, publisher.lastError);
            AsyncGPUReadback.WaitAllRequests();
            Check(publisher.framesSubmitted == 0, "Previous session submitted into the restarted session");
        }
        finally { Destroy(publisher); }
    }

    private static void Tick(TSMPFfmpegRtmpPublisher publisher)
    {
        typeof(TSMPFfmpegRtmpPublisher).GetField("_nextFrameTime", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(publisher, double.MaxValue);
        typeof(TSMPFfmpegRtmpPublisher).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(publisher, null);
    }

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    }

    private static object Call(object target, string name, params object[] args)
    {
        return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);
    }

    private static void Wait(TSMPFfmpegRtmpPublisher publisher, Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition() && timer.ElapsedMilliseconds < 5000)
        {
            Tick(publisher);
            Thread.Sleep(10);
        }
        Check(condition(), "Timed out: " + publisher.lastError + "\n" + publisher.ffmpegOutputTail);
    }

    private static void NewReadbackState()
    {
        var publisher = Create("old-callback");
        try
        {
            publisher.StartPublishing();
            object old = Field(publisher, "_session");
            publisher.StopPublishing();
            publisher.rtmpUrl = Path.Combine(directory, "new-callback.flv");
            publisher.StartPublishing();
            Capture(publisher);
            Check(publisher.readbackInFlight, "No current request");
            Call(publisher, "OnFrameReadbackComplete", old, false, default(AsyncGPUReadbackRequest));
            Check(publisher.readbackInFlight, "Stale callback cleared the new request");
            AsyncGPUReadback.WaitAllRequests();
            Check(publisher.framesSubmitted == 1, "New request did not submit exactly once");
        }
        finally { Destroy(publisher); }
    }

    private static void SourceResize()
    {
        var publisher = Create("resize");
        try
        {
            publisher.flipVertical = true;
            publisher.StartPublishing();
            UnityEngine.Object.DestroyImmediate(publisher.sourceTexture);
            publisher.sourceTexture = new Texture2D(8, 16, TextureFormat.RGBA32, false);
            Capture(publisher);
            Check(!publisher.isPublishing && publisher.framesSubmitted == 0, "Changed shape reached FFmpeg");
            publisher.StartPublishing();
            Check(publisher.isPublishing && publisher.lastFfmpegArguments.Contains("-s 8x16"), publisher.lastError);
            Capture(publisher);
            AsyncGPUReadback.WaitAllRequests();
            Check(publisher.framesSubmitted == 1, publisher.lastError);
        }
        finally { Destroy(publisher); }
    }

    private static void InvalidDimensions()
    {
        var publisher = Create("invalid");
        try
        {
            publisher.useSourceDimensions = false;
            foreach (int value in new[] { 0, -1, int.MaxValue })
            {
                publisher.width = value;
                publisher.StartPublishing();
                Check(!publisher.isPublishing && Field(publisher, "_session") == null, "Invalid width launched FFmpeg");
            }
            publisher.useSourceDimensions = true;
            UnityEngine.Object.DestroyImmediate(publisher.sourceTexture);
            publisher.sourceTexture = new Texture2D(15, 7, TextureFormat.RGBA32, false);
            publisher.StartPublishing();
            Check(!publisher.isPublishing && publisher.lastError.Contains("YUV420p"), "Odd YUV420p accepted");
            publisher.yuv420p = false;
            publisher.StartPublishing();
            Check(publisher.isPublishing, "Odd YUV444p refused: " + publisher.lastError);
        }
        finally { Destroy(publisher); }
    }

    private static void FrameOutput()
    {
        for (int variant = 0; variant < 4; variant++)
        {
            bool flip = (variant & 1) != 0;
            var publisher = Create("pixels-" + variant);
            try
            {
                var texture = (Texture2D)publisher.sourceTexture;
                var pixels = new Color32[texture.width * texture.height];
                for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                {
                    byte value = (byte)(32 + y * 20);
                    pixels[y * texture.width + x] = new Color32(value, value, value, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply();
                if (variant >= 2)
                {
                    var target = new RenderTexture(16, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    target.Create();
                    Graphics.Blit(texture, target);
                    publisher.sourceTexture = target;
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                publisher.flipVertical = flip;
                publisher.useSourceDimensions = flip;
                publisher.width = 16;
                publisher.height = 8;
                publisher.yuv420p = false;
                publisher.extraArguments = "-y -crf 0";
                publisher.StartPublishing();
                Check(publisher.isPublishing, publisher.lastError);
                Capture(publisher);
                AsyncGPUReadback.WaitAllRequests();
                Wait(publisher, () => publisher.framesWritten == 1);
                object session = Field(publisher, "_session");
                byte[] original = (byte[])Field(session, "ReadbackBuffer");
                byte[] expected = flip ? (byte[])Field(session, "FlipBuffer") : original;
                for (int y = 0; y < 8; y++)
                for (int x = 0; x < 16 * 4; x++)
                    Check(expected[y * 64 + x] == original[(flip ? 7 - y : y) * 64 + x], "RGBA row flip corrupted bytes");

                publisher.StopPublishing();
                Check(publisher.lastFfmpegExitCode == 0 && string.IsNullOrEmpty(publisher.lastError), publisher.lastError);
                string raw = Path.Combine(directory, "pixels-" + variant + ".rgba");
                RunProcess(publisher.ffmpegPath, "-hide_banner -loglevel error -y -i \"" + publisher.rtmpUrl +
                    "\" -frames:v 1 -f rawvideo -pix_fmt rgba \"" + raw + "\"");
                byte[] decoded = File.ReadAllBytes(raw);
                Check(decoded.Length == expected.Length, "Encoded frame size differs");
                for (int i = 0; i < decoded.Length; i++)
                    Check(Math.Abs(decoded[i] - expected[i]) <= 2, "FFmpeg pixel mismatch at " + i);
                string probe = RunProcess(Environment.GetEnvironmentVariable("TSMP_VALIDATION_FFPROBE") ?? "ffprobe",
                    "-v error -count_frames -select_streams v:0 -show_entries stream=width,height,nb_read_frames -of csv=p=0 \"" + publisher.rtmpUrl + "\"");
                Check(probe.Trim() == "16,8,1", "Unexpected encoded stream: " + probe);
            }
            finally { Destroy(publisher); }
        }
    }

    private static string RunProcess(string executable, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
        }))
        {
            if (!process.WaitForExit(10000)) { process.Kill(); throw new TimeoutException(executable); }
            string output = process.StandardOutput.ReadToEnd();
            Check(process.ExitCode == 0, executable + " failed: " + process.ExitCode);
            return output;
        }
    }

    private static void RepeatFrame()
    {
        var publisher = Create("repeat");
        try
        {
            publisher.repeatLastFrameWhenIdle = true;
            publisher.StartPublishing();
            object session = Field(publisher, "_session");
            byte[] buffer = (byte[])Field(session, "_writerFrame");
            Capture(publisher);
            AsyncGPUReadback.WaitAllRequests();
            Wait(publisher, () => publisher.framesWritten >= 3);
            Check(publisher.framesSubmitted == 1 && ReferenceEquals(buffer, Field(session, "_writerFrame")), "Repeated frame buffers changed");
            publisher.repeatLastFrameWhenIdle = false;
            Tick(publisher);
            Thread.Sleep(80);
            Tick(publisher);
            int written = publisher.framesWritten;
            Thread.Sleep(80);
            Tick(publisher);
            Check(publisher.framesWritten == written, "Idle repeat did not stop");
        }
        finally { Destroy(publisher); }
    }

    private static void ReadbackMismatch()
    {
        var publisher = Create("wrong-readback-shape");
        var texture = new Texture2D(8, 16, TextureFormat.RGBA32, false);
        try
        {
            publisher.StartPublishing();
            object session = Field(publisher, "_session");
            AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32,
                request => Call(publisher, "OnFrameReadbackComplete", session, true, request));
            AsyncGPUReadback.WaitAllRequests();
            Check(!publisher.isPublishing && publisher.framesSubmitted == 0, "Wrong-shaped GPU data was submitted");
            Check(publisher.lastError.Contains("readback dimensions"), publisher.lastError);
        }
        finally { Destroy(publisher); UnityEngine.Object.DestroyImmediate(texture); }
    }

    private static void Restart()
    {
        var publisher = Create("restart");
        try
        {
            for (int i = 0; i < 6; i++)
            {
                publisher.rtmpUrl = Path.Combine(directory, "restart-" + i + ".flv");
                publisher.StartPublishing();
                Check(publisher.isPublishing, publisher.lastError);
                object session = Field(publisher, "_session");
                var thread = (Thread)Field(session, "_writer");
                int id = ((Process)Field(session, "_process")).Id;
                Capture(publisher);
                AsyncGPUReadback.WaitAllRequests();
                Wait(publisher, () => publisher.framesWritten == 1);
                publisher.StopPublishing();
                publisher.StopPublishing();
                Check(!thread.IsAlive && !ProcessExists(id), "Previous process or thread survived stop");
                Check(!publisher.readbackInFlight && !publisher.isPublishing && string.IsNullOrEmpty(publisher.lastError), publisher.lastError);
            }
        }
        finally { Destroy(publisher); }
    }

    private static bool ProcessExists(int id)
    {
        try { using (var process = Process.GetProcessById(id)) return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static void FailedProcess()
    {
        var publisher = Create("process-error");
        try
        {
            publisher.extraArguments = "-tsmp_invalid_option";
            publisher.StartPublishing();
            Wait(publisher, () => !publisher.isPublishing);
            Check(publisher.lastFfmpegExitCode != 0 && !string.IsNullOrEmpty(publisher.lastError), "Process failure not diagnosed");
            publisher.extraArguments = "-y";
            publisher.StartPublishing();
            Check(publisher.isPublishing && string.IsNullOrEmpty(publisher.lastError), "Cannot restart after failure");
            Check(!publisher.ffmpegOutputTail.Contains("tsmp_invalid_option"), "Old stderr leaked into restart");
        }
        finally { Destroy(publisher); }
    }

    private static void FailedStartup()
    {
        var publisher = Create("missing-executable");
        bool background = Application.runInBackground;
        try
        {
            publisher.ffmpegPath = Path.Combine(directory, "not-installed-ffmpeg.exe");
            publisher.StartPublishing();
            publisher.StopPublishing();
            Check(!publisher.isPublishing && Field(publisher, "_session") == null && !string.IsNullOrEmpty(publisher.lastError), "Failed process start leaked state");
            Check(Application.runInBackground == background, "Failed start changed runInBackground");
            publisher.ffmpegPath = Environment.GetEnvironmentVariable("TSMP_VALIDATION_FFMPEG") ?? "ffmpeg";
            publisher.StartPublishing();
            Check(Application.runInBackground, "Run-in-background override not applied");
            publisher.StopPublishing();
            Check(Application.runInBackground == background, "Stop did not restore runInBackground");
        }
        finally { Destroy(publisher); }
    }

    private static void Disable()
    {
        var publisher = Create("disable");
        try
        {
            publisher.StartPublishing();
            var thread = (Thread)Field(Field(publisher, "_session"), "_writer");
            Capture(publisher);
            publisher.enabled = false;
            AsyncGPUReadback.WaitAllRequests();
            Check(!publisher.isPublishing && !publisher.readbackInFlight && !thread.IsAlive && publisher.framesSubmitted == 0, "Disable left an active session");
            publisher.StartPublishing();
            Check(!publisher.isPublishing, "Disabled component started a session");
            publisher.enabled = true;
            publisher.StartPublishing();
            Check(publisher.isPublishing, "Re-enable cannot restart");
        }
        finally { Destroy(publisher); }
    }

    private static void BlockedWriter()
    {
        Type type = typeof(TSMPFfmpegRtmpPublisher).Assembly.GetType("K13A.TSMP.FfmpegPublishSession");
        object session = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { 1024, 1024, 30, false }, null);
        try
        {
            var info = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 60\"")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true,
                RedirectStandardOutput = true, CreateNoWindow = true
            };
            Call(session, "Start", info);
            int id = ((Process)Field(session, "_process")).Id;
            var thread = (Thread)Field(session, "_writer");
            Call(session, "Submit", new byte[1024 * 1024 * 4]);
            Thread.Sleep(200);
            Check((int)Field(Call(session, "GetStatus"), "Written") == 0 && thread.IsAlive, "Writer did not block on stdin");
            var timer = Stopwatch.StartNew();
            Call(session, "Stop");
            Check(timer.ElapsedMilliseconds < 3000, "Shutdown exceeded bound");
            Check(!thread.IsAlive && !ProcessExists(id), "Blocked writer or child survived shutdown");
            Check((bool)Field(Call(session, "GetStatus"), "Stopped"), "Session did not finish");
            Check(!(bool)Call(session, "Submit", new byte[1024 * 1024 * 4]), "Stopped session accepted a frame");
        }
        finally { Call(session, "Stop"); }
    }
}
