using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace K13A.TSMP
{
    internal sealed class FfmpegPublishSession
    {
        internal readonly int Width;
        internal readonly int Height;
        internal readonly int FrameRate;
        internal readonly byte[] ReadbackBuffer;
        internal readonly byte[] FlipBuffer;
        internal bool ReadbackInFlight;
        internal volatile bool RepeatLastFrame;

        private readonly object _frameLock = new object();
        private readonly object _processLock = new object();
        private readonly AutoResetEvent _frameEvent;
        private readonly byte[] _pendingFrame;
        private readonly byte[] _writerFrame;
        private Process _process;
        private Stream _stream;
        private Thread _writer;
        private bool _stopRequested;
        private bool _finished;
        private bool _hasPendingFrame;
        private bool _hasLastFrame;
        private Status _status;

        internal struct Status
        {
            internal int Submitted;
            internal int Written;
            internal int Dropped;
            internal int ExitCode;
            internal bool Stopped;
            internal string Error;
            internal string Output;
            internal string OutputTail;
        }

        internal FfmpegPublishSession(int width, int height, int frameRate, bool repeatLastFrame)
        {
            Width = width;
            Height = height;
            FrameRate = frameRate;
            RepeatLastFrame = repeatLastFrame;
            int length = checked(width * height * 4);
            ReadbackBuffer = new byte[length];
            FlipBuffer = new byte[length];
            _pendingFrame = new byte[length];
            _writerFrame = new byte[length];
            _frameEvent = new AutoResetEvent(false);
        }

        internal void Start(ProcessStartInfo startInfo)
        {
            _process = new Process { StartInfo = startInfo };
            _process.ErrorDataReceived += OnOutput;
            _process.OutputDataReceived += OnOutput;
            if (!_process.Start())
                throw new InvalidOperationException("Failed to start FFmpeg process.");

            _stream = _process.StandardInput.BaseStream;
            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();
            _writer = new Thread(WriterLoop) { IsBackground = true, Name = "TSMP FFmpeg RTMP Writer" };
            _writer.Start();
        }

        internal Status GetStatus()
        {
            lock (_frameLock)
                return _status;
        }

        internal bool Submit(byte[] frame)
        {
            lock (_frameLock)
            {
                if (_stopRequested || _finished || frame == null || frame.Length != _pendingFrame.Length)
                    return false;

                if (_hasPendingFrame)
                    _status.Dropped++;
                Buffer.BlockCopy(frame, 0, _pendingFrame, 0, frame.Length);
                _hasPendingFrame = true;
                _status.Submitted++;
                _frameEvent.Set();
                return true;
            }
        }

        internal void Stop()
        {
            lock (_frameLock)
            {
                _stopRequested = true;
                _hasPendingFrame = false;
                if (!_finished)
                    _frameEvent.Set();
            }

            if (_writer == null || (_writer.ThreadState & System.Threading.ThreadState.Unstarted) != 0)
            {
                Finish();
                return;
            }

            if (_writer.Join(500))
                return;

            KillProcess();
            if (!_writer.Join(1500))
            {
                lock (_frameLock)
                    _status.Error = "FFmpeg writer shutdown timed out; the previous session is isolated until it exits.";
            }
        }

        private void WriterLoop()
        {
            double interval = 1.0 / Math.Max(1, FrameRate);
            int idleWaitMs = Math.Max(1, (int)Math.Ceiling(interval * 1000.0));
            Stopwatch clock = Stopwatch.StartNew();
            double nextFrameTime = 0;
            try
            {
                while (true)
                {
                    lock (_frameLock)
                    {
                        if (_stopRequested)
                            break;
                    }

                    if (_process.HasExited)
                        throw new IOException("FFmpeg exited with code " + _process.ExitCode + ".");

                    double remaining = nextFrameTime - clock.Elapsed.TotalSeconds;
                    if (remaining > 0)
                    {
                        _frameEvent.WaitOne(Math.Max(1, (int)Math.Ceiling(remaining * 1000.0)));
                        continue;
                    }

                    bool hasFrame;
                    lock (_frameLock)
                    {
                        if (_stopRequested)
                            break;
                        hasFrame = _hasPendingFrame || (RepeatLastFrame && _hasLastFrame);
                        if (_hasPendingFrame)
                        {
                            Buffer.BlockCopy(_pendingFrame, 0, _writerFrame, 0, _writerFrame.Length);
                            _hasPendingFrame = false;
                            _hasLastFrame = true;
                        }
                    }

                    if (!hasFrame)
                    {
                        nextFrameTime = 0;
                        _frameEvent.WaitOne(idleWaitMs);
                        continue;
                    }

                    if (nextFrameTime == 0)
                        nextFrameTime = clock.Elapsed.TotalSeconds;
                    _stream.Write(_writerFrame, 0, _writerFrame.Length);
                    lock (_frameLock)
                        _status.Written++;
                    nextFrameTime += interval;
                    double now = clock.Elapsed.TotalSeconds;
                    if (nextFrameTime <= now)
                        nextFrameTime = now + interval;
                }
            }
            catch (Exception exception)
            {
                lock (_frameLock)
                {
                    if (!_stopRequested)
                        _status.Error = "FFmpeg writer failed: " + exception.Message;
                }
            }
            finally
            {
                Finish();
            }
        }

        private void OnOutput(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrEmpty(args.Data))
                return;

            lock (_frameLock)
            {
                if (_stopRequested || _finished)
                    return;
                _status.Output = args.Data;
                string tail = string.IsNullOrEmpty(_status.OutputTail) ? args.Data : _status.OutputTail + "\n" + args.Data;
                _status.OutputTail = tail.Length > 4096 ? tail.Substring(tail.Length - 4096) : tail;
            }
        }

        private void KillProcess()
        {
            lock (_processLock)
            {
                try
                {
                    if (_process != null && !_process.HasExited)
                        _process.Kill();
                }
                catch (Exception exception)
                {
                    lock (_frameLock)
                        _status.Error = "FFmpeg shutdown failed: " + exception.Message;
                }
            }
        }

        private void Finish()
        {
            lock (_processLock)
            {
                if (_process != null)
                {
                    try
                    {
                        if (!_process.HasExited)
                        {
                            _stream?.Close();
                            if (!_process.WaitForExit(500))
                            {
                                _process.Kill();
                                _process.WaitForExit(500);
                            }
                        }
                        if (_process.HasExited)
                        {
                            lock (_frameLock)
                                _status.ExitCode = _process.ExitCode;
                        }
                    }
                    catch (InvalidOperationException) when (_stream == null)
                    {
                    }
                    catch (Exception exception)
                    {
                        lock (_frameLock)
                        {
                            if (string.IsNullOrEmpty(_status.Error))
                                _status.Error = "FFmpeg cleanup failed: " + exception.Message;
                        }
                        KillProcess();
                    }
                    finally
                    {
                        _process.ErrorDataReceived -= OnOutput;
                        _process.OutputDataReceived -= OnOutput;
                        _process.Dispose();
                        _process = null;
                    }
                }
            }

            lock (_frameLock)
            {
                if (!_finished)
                    _frameEvent.Dispose();
                _finished = true;
                _status.Stopped = true;
            }
        }
    }
}
