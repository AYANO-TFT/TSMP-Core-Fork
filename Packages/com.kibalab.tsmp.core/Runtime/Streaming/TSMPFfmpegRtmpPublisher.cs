using System;
using System.Diagnostics;
using System.Net.Sockets;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace K13A.TSMP
{
    [ExecuteAlways]
    public sealed class TSMPFfmpegRtmpPublisher : MonoBehaviour
    {
        [Header("Source")]
        public Texture sourceTexture;
        public bool useSourceDimensions = true;
        public int width = 640;
        public int height = 360;
        public int frameRate = 30;
        public bool flipVertical;

        [Header("FFmpeg")]
        public string ffmpegPath = "ffmpeg";
        public string rtmpUrl = "rtmp://127.0.0.1:1935/tsmp";
        public bool useServerAndStreamKey;
        public string rtmpServerUrl = "rtmp://127.0.0.1:1935/live";
        public string streamKey = "tsmp";
        public int videoBitrateKbps = 4000;
        public string preset = "veryfast";
        public bool yuv420p = true;
        public string extraArguments = string.Empty;
        public bool checkRtmpTcpBeforeStart = true;
        public int tcpConnectTimeoutMs = 1000;
        public bool repeatLastFrameWhenIdle = true;

        [Header("Lifecycle")]
        public bool autoStart;
        public bool publishInEditMode;
        public bool forceRunInBackground = true;
        public bool logFfmpegOutput;

        [Header("Diagnostics")]
        public bool isPublishing;
        public bool readbackInFlight;
        public int framesSubmitted;
        public int framesWritten;
        public int framesDropped;
        public int lastFfmpegExitCode;
        public string lastFfmpegArguments;
        public string lastError;
        public string lastFfmpegOutput;
        [TextArea(3, 8)] public string ffmpegOutputTail;

        private FfmpegPublishSession _session;
        private double _nextFrameTime;
        private int _activeWidth;
        private int _activeHeight;
        private bool _previousRunInBackground;
        private bool _didOverrideRunInBackground;

        private void OnEnable()
        {
            _nextFrameTime = 0.0;

            if (autoStart && (Application.isPlaying || publishInEditMode))
                StartPublishing();
        }

        private void OnDisable()
        {
            StopPublishing();
        }

        private void OnDestroy()
        {
            StopPublishing();
        }

        private void OnValidate()
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            frameRate = Mathf.Clamp(frameRate, 1, 120);
            videoBitrateKbps = Mathf.Max(64, videoBitrateKbps);
            tcpConnectTimeoutMs = Mathf.Clamp(tcpConnectTimeoutMs, 100, 10000);
            if (string.IsNullOrWhiteSpace(preset))
                preset = "veryfast";
        }

        private void Update()
        {
            FfmpegPublishSession session = _session;
            if (session == null)
                return;

            CopyDiagnostics(session);
            if (session.GetStatus().Stopped)
            {
                StopPublishing();
                return;
            }
            session.RepeatLastFrame = repeatLastFrameWhenIdle;

            if (!Application.isPlaying && !publishInEditMode)
                return;

            double now = Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble;
            double interval = 1.0 / session.FrameRate;
            if (now < _nextFrameTime)
                return;

            _nextFrameTime = now + interval;
            RequestFrame();
        }

        [ContextMenu("Start Publishing")]
        public void StartPublishing()
        {
            if (_session != null)
            {
                if (!_session.GetStatus().Stopped)
                    return;
                StopPublishing();
            }

            lastError = string.Empty;
            isPublishing = false;
            readbackInFlight = false;

            if (!ValidateSetup() || !ResolveActiveDimensions())
                return;

            ResetCounters();

            if (checkRtmpTcpBeforeStart && !CheckRtmpTcpEndpoint())
                return;

            try
            {
                lastFfmpegArguments = BuildFfmpegArguments();
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = lastFfmpegArguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                _session = new FfmpegPublishSession(_activeWidth, _activeHeight, Mathf.Clamp(frameRate, 1, 120), repeatLastFrameWhenIdle);
                _session.Start(startInfo);
                ApplyRunInBackgroundOverride();

                isPublishing = true;
                _nextFrameTime = 0.0;
            }
            catch (Exception ex)
            {
                SetError("Failed to start FFmpeg: " + ex.Message);
                StopPublishing();
            }
        }

        [ContextMenu("Stop Publishing")]
        public void StopPublishing()
        {
            FfmpegPublishSession session = _session;
            _session = null;
            readbackInFlight = false;
            isPublishing = false;
            try
            {
                if (session != null)
                {
                    session.Stop();
                    CopyDiagnostics(session);
                }
            }
            finally
            {
                RestoreRunInBackgroundOverride();
            }
        }

        private bool ValidateSetup()
        {
            if (!isActiveAndEnabled)
            {
                SetError("Enable the publisher component and its GameObject before starting.");
                return false;
            }

            if (sourceTexture == null)
            {
                SetError("Source texture is not assigned.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(ffmpegPath))
            {
                SetError("FFmpeg path is empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(GetEffectiveRtmpUrl()))
            {
                SetError("RTMP URL is empty.");
                return false;
            }

            return true;
        }

        private bool ResolveActiveDimensions()
        {
            if (useSourceDimensions && sourceTexture != null)
            {
                _activeWidth = sourceTexture.width;
                _activeHeight = sourceTexture.height;
            }
            else
            {
                _activeWidth = width;
                _activeHeight = height;
            }

            if (sourceTexture.dimension != TextureDimension.Tex2D || _activeWidth <= 0 || _activeHeight <= 0 ||
                (long)_activeWidth * _activeHeight > int.MaxValue / 4)
            {
                SetError("FFmpeg requires a 2D source with valid RGBA32 dimensions.");
                return false;
            }
            if (sourceTexture.width != _activeWidth || sourceTexture.height != _activeHeight)
            {
                SetError("Source dimensions " + sourceTexture.width + "x" + sourceTexture.height +
                    " do not match FFmpeg dimensions " + _activeWidth + "x" + _activeHeight +
                    ". Enable Use Source Dimensions or use matching dimensions; TSMP pixels are not resized.");
                return false;
            }
            if (yuv420p && ((_activeWidth & 1) != 0 || (_activeHeight & 1) != 0))
            {
                SetError("YUV420p requires even width and height. Use even source dimensions or disable YUV420p.");
                return false;
            }
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                SetError("This graphics device does not support AsyncGPUReadback.");
                return false;
            }
            return true;
        }

        private void ResetCounters()
        {
            framesSubmitted = 0;
            framesWritten = 0;
            framesDropped = 0;
            lastFfmpegExitCode = 0;
            lastFfmpegArguments = string.Empty;
            lastFfmpegOutput = string.Empty;
            ffmpegOutputTail = string.Empty;
        }

        private void RequestFrame()
        {
            FfmpegPublishSession session = _session;
            if (session == null || session.ReadbackInFlight)
                return;

            if (session.GetStatus().Stopped)
            {
                CopyDiagnostics(session);
                StopPublishing();
                return;
            }

            if (sourceTexture == null || sourceTexture.dimension != TextureDimension.Tex2D ||
                sourceTexture.width != session.Width || sourceTexture.height != session.Height)
            {
                SetError("Source texture was removed or its dimensions changed. Restart publishing with matching dimensions.");
                StopPublishing();
                return;
            }

            session.ReadbackInFlight = true;
            readbackInFlight = true;
            bool flip = flipVertical;
            try
            {
                AsyncGPUReadback.Request(sourceTexture, 0, TextureFormat.RGBA32,
                    request => OnFrameReadbackComplete(session, flip, request));
            }
            catch (Exception exception)
            {
                SetError("AsyncGPUReadback request failed: " + exception.Message);
                StopPublishing();
            }
        }

        private void OnFrameReadbackComplete(FfmpegPublishSession session, bool flip, AsyncGPUReadbackRequest request)
        {
            if (!ReferenceEquals(_session, session))
                return;

            session.ReadbackInFlight = false;
            readbackInFlight = false;
            if (request.hasError)
            {
                SetError("AsyncGPUReadback failed.");
                return;
            }

            try
            {
                NativeArray<byte> data = request.GetData<byte>();
                if (request.width != session.Width || request.height != session.Height ||
                    data.Length != session.ReadbackBuffer.Length)
                {
                    SetError("GPU readback dimensions do not match the active FFmpeg session. The frame was discarded.");
                    StopPublishing();
                    return;
                }

                data.CopyTo(session.ReadbackBuffer);
                byte[] source = session.ReadbackBuffer;
                if (flip)
                {
                    FlipFrameRows(source, session.FlipBuffer, session.Width, session.Height, 4);
                    source = session.FlipBuffer;
                }
                session.Submit(source);
                CopyDiagnostics(session);
            }
            catch (Exception exception)
            {
                SetError("GPU frame submission failed: " + exception.Message);
                StopPublishing();
            }
        }

        private void ApplyRunInBackgroundOverride()
        {
            if (!forceRunInBackground || _didOverrideRunInBackground)
                return;

            _previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            _didOverrideRunInBackground = true;
        }

        private void RestoreRunInBackgroundOverride()
        {
            if (!_didOverrideRunInBackground)
                return;

            Application.runInBackground = _previousRunInBackground;
            _didOverrideRunInBackground = false;
        }

        private string BuildFfmpegArguments()
        {
            int gop = Mathf.Clamp(frameRate, 1, 120);
            int bufferKbps = Mathf.Max(64, videoBitrateKbps / 2);
            string pixelFormat = yuv420p ? "yuv420p" : "yuv444p";
            string extra = string.IsNullOrWhiteSpace(extraArguments) ? string.Empty : " " + extraArguments.Trim();

            return "-hide_banner -loglevel warning " +
                "-f rawvideo -pix_fmt rgba " +
                "-s " + _activeWidth + "x" + _activeHeight + " " +
                "-r " + gop + " " +
                "-i pipe:0 " +
                "-an " +
                "-c:v libx264 " +
                "-preset " + preset + " " +
                "-tune zerolatency " +
                "-pix_fmt " + pixelFormat + " " +
                "-b:v " + videoBitrateKbps + "k " +
                "-maxrate " + videoBitrateKbps + "k " +
                "-bufsize " + bufferKbps + "k " +
                "-g " + gop + " " +
                "-keyint_min " + gop + " " +
                "-sc_threshold 0 " +
                "-bf 0" +
                extra + " " +
                "-flvflags no_duration_filesize " +
                "-f flv " +
                QuoteArgument(GetEffectiveRtmpUrl());
        }

        private bool CheckRtmpTcpEndpoint()
        {
            string effectiveUrl = GetEffectiveRtmpUrl();
            if (!Uri.TryCreate(effectiveUrl, UriKind.Absolute, out Uri uri))
            {
                SetError("RTMP URL is invalid.");
                return false;
            }

            int port = uri.Port > 0 ? uri.Port : 1935;
            try
            {
                using (var client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect(uri.Host, port, null, null);
                    bool connected;
                    using (var waitHandle = result.AsyncWaitHandle)
                        connected = waitHandle.WaitOne(Mathf.Clamp(tcpConnectTimeoutMs, 100, 10000));
                    if (!connected)
                    {
                        SetError("RTMP TCP connect timed out: " + uri.Host + ":" + port);
                        return false;
                    }

                    client.EndConnect(result);
                    return true;
                }
            }
            catch (Exception ex)
            {
                SetError("RTMP TCP connect failed: " + uri.Host + ":" + port + " (" + ex.Message + ")");
                return false;
            }
        }

        private string GetEffectiveRtmpUrl()
        {
            if (!useServerAndStreamKey)
                return rtmpUrl;

            string server = string.IsNullOrWhiteSpace(rtmpServerUrl) ? string.Empty : rtmpServerUrl.Trim();
            string key = string.IsNullOrWhiteSpace(streamKey) ? string.Empty : streamKey.Trim();

            if (string.IsNullOrEmpty(server))
                return string.Empty;

            if (string.IsNullOrEmpty(key))
                return server;

            return server.TrimEnd('/') + "/" + key.TrimStart('/');
        }

        private void CopyDiagnostics(FfmpegPublishSession session)
        {
            FfmpegPublishSession.Status status = session.GetStatus();
            framesSubmitted = status.Submitted;
            framesWritten = status.Written;
            framesDropped = status.Dropped;
            lastFfmpegExitCode = status.ExitCode;
            if (!string.IsNullOrEmpty(status.Error))
                SetError(status.Error);
            if (logFfmpegOutput && !string.IsNullOrEmpty(status.Output) && status.Output != lastFfmpegOutput)
                Debug.Log("[TSMP] " + status.Output);
            lastFfmpegOutput = status.Output ?? string.Empty;
            ffmpegOutputTail = status.OutputTail ?? string.Empty;
        }

        private void SetError(string error)
        {
            if (lastError == error)
                return;
            lastError = error;
            Debug.LogWarning("[TSMP FFmpeg] " + error, this);
        }

        private static void FlipFrameRows(byte[] source, byte[] destination, int width, int height, int bytesPerPixel)
        {
            int rowBytes = width * bytesPerPixel;
            for (int y = 0; y < height; y++)
            {
                int sourceOffset = y * rowBytes;
                int destinationOffset = (height - 1 - y) * rowBytes;
                Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, rowBytes);
            }
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
