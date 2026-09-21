#if !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using K13A.TSMP;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

public static class ResourceProfile
{
    const int Capacity = 16384;
    sealed class Series
    {
        public readonly double[] Micros = new double[Capacity];
        public readonly long[] Bytes = new long[Capacity];
        public int Count;
    }
    static readonly Dictionary<string, Series> Timings = new Dictionary<string, Series>();
    static bool collecting;
    static bool allocationCounterValid;

    public readonly struct Scope : IDisposable
    {
        readonly Series series;
        readonly long start;
        readonly long allocated;
        internal Scope(string name)
        {
            if (!Timings.TryGetValue(name, out series))
            {
                series = new Series();
                Timings.Add(name, series);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread();
            start = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (series == null) return;
            long end = Stopwatch.GetTimestamp();
            long bytes = allocationCounterValid ? GC.GetAllocatedBytesForCurrentThread() - allocated : -1;
            int index = series.Count;
            if (index >= Capacity) throw new InvalidOperationException("Profile sample capacity exceeded");
            series.Micros[index] = (end - start) * 1000000.0 / Stopwatch.Frequency;
            series.Bytes[index] = bytes;
            series.Count++;
        }
    }
    public static Scope Time(string name) => collecting ? new Scope(name) : default;
    static string N(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
    static double P(IEnumerable<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * p))];
    }

    public static IEnumerator Measure(ContinuousFrameValidation.Case test, ContinuousFrameValidation.ILoopback loop,
        Action<Action> schedule, string resultRoot)
    {
        string root = Path.Combine(resultRoot, test.Name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "hardware.txt"), "CPU=" + SystemInfo.processorType + "\nCPUThreads=" + SystemInfo.processorCount +
            "\nSystemMemoryMB=" + SystemInfo.systemMemorySize + "\nGraphicsMemoryMB=" + SystemInfo.graphicsMemorySize +
            "\nGraphicsMultithreaded=" + SystemInfo.graphicsMultiThreaded + "\nOS=" + SystemInfo.operatingSystem);
        long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        var calibration = new byte[8192];
        long allocationDelta = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        GC.KeepAlive(calibration);
        allocationCounterValid = allocationDelta >= 8192;
        File.WriteAllText(Path.Combine(root, "allocation-counter.txt"), "AllocatedBytesForCurrentThread valid=" + allocationCounterValid +
            "\n8192-byte control delta=" + allocationDelta + "\nInvalid scoped allocations are reported as -1. Use Unity GC counters instead.");
        var packet = new byte[test.Bytes];
        int sequence = 0;
        Action publish = () =>
        {
            int id = ++sequence;
            packet[0] = (byte)id;
            packet[1] = (byte)(id >> 8);
            packet[2] = (byte)(id >> 16);
            int end = packet.Length - 4;
            packet[end] = (byte)(255 - packet[0]);
            packet[end + 1] = (byte)(255 - packet[1]);
            packet[end + 2] = (byte)(255 - packet[2]);
            using (Time("Host.Publish")) loop.Publish(packet);
        };
        loop.SetAutomatic(false);
        publish();
        loop.Decode();
        double deadline = TimeNow + 30;
        while (loop.Busy && TimeNow < deadline) yield return null;
        if (loop.Busy || loop.AppliedCount != 1) throw new InvalidOperationException("Profile preflight failed: " + loop.Diagnostics);

        Action step = () =>
        {
            publish();
            using (Time("Host.DecodeNow")) loop.Decode();
        };
        Timings.Clear();
        collecting = true;
        schedule(step);
        double warmEnd = TimeNow + 3;
        while (TimeNow < warmEnd) yield return null;
        collecting = false;
        DumpMemory(loop, root, "warm");
        DumpAvailableMarkers(root);
        var counters = OpenCounters();
        var counterValues = counters.Keys.ToDictionary(name => name, name => new List<double>(4096));
        var frameTimes = new List<double>(4096);
        int gcStart = GC.CollectionCount(0);
        long managedStart = GC.GetTotalMemory(false);
        int sentStart = sequence;
        int appliedStart = loop.AppliedCount;
        int busyStart = Convert.ToInt32(loop.ReadDecoder("skippedBusyDecodeCount"));
        foreach (var series in Timings.Values) series.Count = 0;
        collecting = true;
        double begin = TimeNow;
        double previous = begin;
        double duration = Environment.GetEnvironmentVariable("TSMP_PROFILE_GPU_ONLY") == "1" ? .5 : test.Seconds;
        while (TimeNow < begin + duration)
        {
            yield return null;
            double now = TimeNow;
            frameTimes.Add((now - previous) * 1000);
            previous = now;
            foreach (var item in counters)
                if (item.Value.Valid && item.Value.Count > 0)
                    counterValues[item.Key].Add(item.Value.LastValue);
        }
        collecting = false;
        int sent = sequence - sentStart;
        int appliedInWindow = loop.AppliedCount - appliedStart;
        int gcEnd = GC.CollectionCount(0);
        long managedEnd = GC.GetTotalMemory(false);
        double seconds = TimeNow - begin;
        DumpMemory(loop, root, "end");
        var rows = new List<string> { "scope,calls,meanUs,p50Us,p95Us,maxUs,totalMs,meanAllocatedBytes,totalAllocatedBytes" };
        foreach (var item in Timings.OrderBy(item => item.Key))
        {
            Series data = item.Value;
            double[] values = data.Micros.Take(data.Count).ToArray();
            long bytes = data.Bytes.Take(data.Count).Sum();
            rows.Add(string.Join(",", item.Key, data.Count, N(values.Average()), N(P(values, .5)), N(P(values, .95)), N(values.Max()),
                N(values.Sum() / 1000), N((double)bytes / data.Count), allocationCounterValid ? bytes : -1));
        }
        File.WriteAllLines(Path.Combine(root, "cpu.csv"), rows);
        rows = new List<string> { "counter,samples,mean,p50,p95,min,max,first,last" };
        foreach (var item in counters)
        {
            var values = counterValues[item.Key];
            rows.Add(values.Count == 0 ? item.Key + ",0" : string.Join(",", item.Key, values.Count, N(values.Average()),
                N(P(values, .5)), N(P(values, .95)), N(values.Min()), N(values.Max()), N(values.First()), N(values.Last())));
            item.Value.Dispose();
        }
        File.WriteAllLines(Path.Combine(root, "counters.csv"), rows);
        int lastMeasured = sequence;
        double tail = TimeNow + 1;
        while (TimeNow < tail) yield return null;
        schedule(null);
        deadline = TimeNow + 10;
        while (loop.Busy && TimeNow < deadline) yield return null;
        int received = 0;
        for (int i = 0; i < loop.AppliedCount; i++)
            if (loop.AppliedIds[i] > sentStart && loop.AppliedIds[i] <= lastMeasured) received++;
        if (loop.Busy || loop.CorruptCount != 0 || !string.IsNullOrEmpty(loop.Error) || !string.IsNullOrEmpty(loop.DecoderError))
            throw new InvalidOperationException("Profile loopback failed: " + loop.Diagnostics);
        File.WriteAllText(Path.Combine(root, "delivery.txt"), "seconds=" + N(seconds) + "\npublications=" + sent +
            "\nreceived=" + received + "\nappliedInWindow=" + appliedInWindow + "\nloopHz=" + N(frameTimes.Count / seconds) +
            "\nframeP95ms=" + N(P(frameTimes, .95)) + "\nbusyTicks=" + (Convert.ToInt32(loop.ReadDecoder("skippedBusyDecodeCount")) - busyStart) +
            "\npayloadBytes=" + loop.PayloadBytes + "\nGCCollections=" + (gcEnd - gcStart) + "\nmanagedStart=" + managedStart + "\nmanagedEnd=" + managedEnd +
            "\npredicted=" + loop.ReadDecoder("predictedReadbackCount") + "\nfallbacks=" + loop.ReadDecoder("predictionFallbackCount"));
        if (ContinuousFrameValidation.UdonFactory == null)
        {
            var gpu = MeasureGpu(loop, root);
            while (gpu.MoveNext()) yield return gpu.Current;
        }
        var helpers = loop.ProfileHelpers(Math.Max(FrameHeader.Size, test.Bytes), test.Width, test.Height);
        foreach (var helper in helpers.Values)
            for (int i = 0; i < 20; i++) helper();
        Timings.Clear();
        collecting = true;
        foreach (var helper in helpers)
            for (int i = 0; i < 200; i++)
                using (Time("Host.Helper." + helper.Key)) helper.Value();
        collecting = false;
        rows = new List<string> { "scope,calls,meanUs,p50Us,p95Us" };
        foreach (var item in Timings.OrderBy(item => item.Key))
        {
            var values = item.Value.Micros.Take(item.Value.Count).Skip(1).ToArray();
            rows.Add(string.Join(",", item.Key, values.Length, N(values.Average()), N(P(values, .5)), N(P(values, .95))));
        }
        File.WriteAllLines(Path.Combine(root, "helpers.csv"), rows);
        Debug.Log("RESOURCE PROFILE " + test.Name + " sent=" + sent + " applied=" + received);
        loop.Stop();
    }

    static double TimeNow => UnityEngine.Time.realtimeSinceStartupAsDouble;

    static Dictionary<string, ProfilerRecorder> OpenCounters()
    {
        var result = new Dictionary<string, ProfilerRecorder>();
        foreach (string name in new[] { "GC Allocated In Frame", "GC Used Memory", "GC Reserved Memory", "Total Used Memory", "Total Reserved Memory", "Gfx Used Memory", "Render Textures Bytes", "Render Textures Count" })
            result[name] = ProfilerRecorder.StartNew(ProfilerCategory.Memory, name, 1);
        foreach (string name in new[] { "Draw Calls Count", "SetPass Calls Count", "Batches Count" })
            result[name] = ProfilerRecorder.StartNew(ProfilerCategory.Render, name, 1);
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (var handle in handles)
        {
            var info = ProfilerRecorderHandle.GetDescription(handle);
            if (info.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds &&
                (info.Name.IndexOf("Readback", StringComparison.OrdinalIgnoreCase) >= 0 || info.Name == "GC.Collect" ||
                 info.Name == "Graphics.Blit" || info.Name == "Texture2D.Apply" || info.Name == "Main Thread" || info.Name == "Render Thread"))
                result[info.Name] = ProfilerRecorder.StartNew(info.Category, info.Name, 1);
        }
        return result;
    }

    static void DumpAvailableMarkers(string root)
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        File.WriteAllLines(Path.Combine(root, "markers.txt"), handles.Select(ProfilerRecorderHandle.GetDescription)
            .Select(info => info.Category.Name + " | " + info.Name + " | " + info.UnitType));
    }

    static void DumpMemory(ContinuousFrameValidation.ILoopback loop, string root, string phase)
    {
        var seen = new HashSet<object>();
        var rows = new List<string> { "field,type,lengthOrWidth,height,storageBytes,runtimeBytes" };
        Action<string, object> visit = null;
        visit = (name, value) =>
        {
            if (value == null || !seen.Add(value)) return;
            if (value is RenderTexture rt)
            {
                long bytes = (long)rt.width * rt.height * UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(rt.graphicsFormat);
                rows.Add(string.Join(",", name, rt.graphicsFormat, rt.width, rt.height, bytes, Profiler.GetRuntimeMemorySizeLong(rt)));
            }
            else if (value is Texture2D texture)
            {
                long bytes=(long)texture.width*texture.height*UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(texture.graphicsFormat);
                rows.Add(string.Join(",",name,texture.graphicsFormat,texture.width,texture.height,bytes,Profiler.GetRuntimeMemorySizeLong(texture)));
            }
            else if (value is Array array)
            {
                Type element = array.GetType().GetElementType();
                if (element.IsArray || element == typeof(object) || typeof(UnityEngine.Object).IsAssignableFrom(element))
                {
                    for (int i = 0; i < array.Length; i++) visit(name + "[" + i + "]", array.GetValue(i));
                }
                else
                {
                    int size = element == typeof(Color32) ? 4 : element == typeof(byte) || element == typeof(bool) ? 1 :
                        element == typeof(int) || element == typeof(uint) || element == typeof(float) ? 4 : IntPtr.Size;
                    rows.Add(string.Join(",", name, element.Name + "[]", array.Length, 0, (long)array.Length * size, 0));
                }
            }
        };
        foreach (string name in new[] { "_slotSnapshots", "_slotHeaderTextures", "_slotCombinedTextures", "_slotByteTextures", "_slotReadbackBytes", "_slotHeaders", "_slotPayloads", "_slotOptions", "_slotPredictedHeaders", "_rawByteValueArrays", "_crc32Table", "_predictionHeader" })
            visit(name, loop.ReadDecoder(name));
        visit("encoder.output", loop.Output);
        foreach(string name in new[] { "_gpuUpload","_gpuUploadBytes","_gpuSymbols","_stagingTexture","_rasterPixels","outputTexture","_pixels","_basePixels" })
            visit("encoder."+name,loop.ReadEncoder(name));
        File.WriteAllText(Path.Combine(root,"encoder-path.txt"),"GPU Luma4="+loop.ReadEncoder("lastFrameUsedGpuLuma4"));
        File.WriteAllLines(Path.Combine(root, "memory-" + phase + ".csv"), rows);
    }

    static IEnumerator MeasureGpu(ContinuousFrameValidation.ILoopback loop, string root)
    {
        var snapshots = (RenderTexture[])loop.ReadDecoder("_slotSnapshots");
        var headers = (RenderTexture[])loop.ReadDecoder("_slotHeaderTextures");
        var combined = (RenderTexture[])loop.ReadDecoder("_slotCombinedTextures");
        int slot = Array.FindIndex(combined, texture => texture != null);
        if (slot < 0) throw new InvalidOperationException("No predicted GPU textures to profile");
        var handlers = (TSMPCodec[])loop.ReadDecoder("codecHandlers");
        TSMPCodec codec = handlers.First(handler => handler.codecId == 0);
        var headerMaterial = new Material(codec.selectedDecodeMaterial);
        var payloadMaterial = new Material(codec.selectedDecodeMaterial);
        headerMaterial.SetFloat("_StartBlock", 2 * (int)loop.ReadDecoder("_activeWidthBlocks"));
        headerMaterial.SetFloat("_ByteCount", FrameHeader.Size);
        headerMaterial.SetFloat("_OutputWidth", headers[slot].width);
        headerMaterial.SetFloat("_OutputHeight", headers[slot].height);
        headerMaterial.SetFloat("_TSMPHeaderPixels", 0);
        headerMaterial.SetTexture("_TSMPHeaderTex", null);
        payloadMaterial.SetTexture("_TSMPHeaderTex", headers[slot]);
        var lut = payloadMaterial.GetTexture("_CalibrationLut") as RenderTexture;
        bool prepareLut = payloadMaterial.IsKeywordEnabled("TSMP_CALIBRATION_LUT") && lut != null;
        Material headerCalibration = prepareLut ? new Material(codec.calibrationMaterial) : null;
        Material payloadCalibration = prepareLut ? new Material(codec.calibrationMaterial) : null;
        if (prepareLut)
        {
            headerCalibration.CopyPropertiesFromMaterial(headerMaterial);
            payloadCalibration.CopyPropertiesFromMaterial(payloadMaterial);
        }
        var expectedPayload = ((byte[])loop.ReadDecoder("_payloadBytes")).Take(loop.PayloadBytes).ToArray();
        var rows = new List<string> { "pass,samples,repeats,gpuP50us,gpuP95us,gpuMinUs,gpuMaxUs" };
        var trace = new List<string> { "pass,iteration,frame,enabled,cpuBlocks,gpuBlocks,gpuNanoseconds" };
        int previousRate = Application.targetFrameRate;
        Application.targetFrameRate = -1;
        Camera camera = UnityEngine.Object.FindObjectOfType<Camera>();
#if UNITY_EDITOR
        UnityEditorInternal.ProfilerDriver.SetAreaEnabled(ProfilerArea.GPU, true);
#endif
        Profiler.enabled = true;
        Profiler.SetAreaEnabled(ProfilerArea.GPU, true);
        File.WriteAllText(Path.Combine(root, "gpu-support.txt"), "supportsGpuRecorder=" + SystemInfo.supportsGpuRecorder +
            "\nProfiler.enabled=" + Profiler.enabled + "\nGPU area=" + Profiler.GetAreaEnabled(ProfilerArea.GPU));
        foreach (string name in new[] { "snapshot", "header", "payload-with-header", "decoder-total" })
        {
            var sampler = CustomSampler.Create("TSMP Resource " + name, true);
            var recorder = sampler.GetRecorder();
            recorder.enabled = true;
            const int repeats = 32;
            var commands = new CommandBuffer();
            commands.BeginSample(sampler);
            for (int i = 0; i < repeats; i++)
            {
                if (name == "snapshot" || name == "decoder-total") commands.Blit(loop.Output, snapshots[slot]);
                if (name == "header" || name == "decoder-total")
                {
                    if (prepareLut) commands.Blit(snapshots[slot], lut, headerCalibration);
                    commands.Blit(snapshots[slot], headers[slot], headerMaterial);
                }
                if (name == "payload-with-header" || name == "decoder-total")
                {
                    if (prepareLut) commands.Blit(snapshots[slot], lut, payloadCalibration);
                    commands.Blit(snapshots[slot], combined[slot], payloadMaterial);
                }
            }
            commands.EndSample(sampler);
            var samples = new List<double>();
            for (int frame = 0; frame < 40; frame++)
            {
                Profiler.enabled = true;
                Graphics.ExecuteCommandBuffer(commands);
                var request = AsyncGPUReadback.Request(combined[slot], 0);
                request.WaitForCompletion();
                if (request.hasError) throw new InvalidOperationException("GPU profile readback failed");
                if (name == "decoder-total" && frame == 39)
                {
                    var pixels = request.GetData<Color32>().ToArray();
                    var actual = new byte[expectedPayload.Length];
                    if (!ByteTextureReader.CopyBytesAtPixel(pixels, FrameHeader.Size / 4, actual, actual.Length) ||
                        !actual.SequenceEqual(expectedPayload))
                        throw new InvalidOperationException("GPU timing replay changed decoded payload");
                }
                if (camera != null) camera.Render();
                yield return null;
                trace.Add(string.Join(",", name, frame, UnityEngine.Time.frameCount, Profiler.enabled,
                    recorder.sampleBlockCount, recorder.gpuSampleBlockCount, recorder.gpuElapsedNanoseconds));
                if (frame >= 15 && recorder.gpuSampleBlockCount == 1 && recorder.gpuElapsedNanoseconds > 0)
                    samples.Add(recorder.gpuElapsedNanoseconds / 1000.0 / repeats);
            }
            rows.Add(samples.Count == 0 ? name + ",0," + repeats : string.Join(",", name, samples.Count, repeats,
                N(P(samples, .5)), N(P(samples, .95)), N(samples.Min()), N(samples.Max())));
            recorder.enabled = false;
            commands.Dispose();
        }
        Profiler.enabled = false;
        Application.targetFrameRate = previousRate;
        UnityEngine.Object.Destroy(headerMaterial);
        UnityEngine.Object.Destroy(payloadMaterial);
        if (headerCalibration != null) UnityEngine.Object.Destroy(headerCalibration);
        if (payloadCalibration != null) UnityEngine.Object.Destroy(payloadCalibration);
        File.WriteAllLines(Path.Combine(root, "gpu.csv"), rows);
        File.WriteAllLines(Path.Combine(root, "gpu-trace.csv"), trace);
        File.WriteAllText(Path.Combine(root, "gpu-replay.txt"), "PASS replay matches production decoded payload\nCalibration preparation included=" + prepareLut);
    }
}
#endif
