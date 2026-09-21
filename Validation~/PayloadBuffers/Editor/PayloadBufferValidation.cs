using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using Unity.Profiling;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using VRC.Udon.Editor;
#endif
using Object = UnityEngine.Object;

public static class PayloadBufferValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static byte[] buffer;
    static readonly byte[] Source = new byte[4096];

#if UDONSHARP
    public static void RunUdon()
    {
        try
        {
            const string path = "Assets/PayloadBuffers/PayloadBufferVmProbe.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/PayloadBuffers/PayloadBufferVmProbe.cs");
                AssetDatabase.CreateAsset(asset, path);
            }
            bool failed = false;
            Application.LogCallback listener = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception) failed = true; };
            Application.logMessageReceived += listener;
            try { UdonSharpCompilerV1.CompileSync(new UdonSharpCompileOptions { IsEditorBuild = false }); }
            finally { Application.logMessageReceived -= listener; }
            Check(!failed, "Full Udon client compilation");
            var code = asset.SerializedProgramAsset.RetrieveProgram();
            var vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(code);
            void Set<T>(string name, T value) => code.Heap.SetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name), value, typeof(T));
            T Get<T>(string name) => (T)code.Heap.GetHeapVariable(code.SymbolTable.GetAddressFromSymbol(name));
            void Call(string name)
            {
                vm.SetProgramCounter(code.EntryPoints.GetAddressFromSymbol(name));
                Check(vm.Interpret() == 0, "VM " + name);
            }
            byte[] capacity = null;
            foreach (int count in new[] { 0, 8, 2048, 2065, 2048, 0, 65535, 0, 1, 7, 8, 65534, 65535 })
            {
                Set("byteCount", count);
                Call("EnsureCapacity");
                byte[] current = Get<byte[]>("buffer");
                Check(current.Length >= count, "VM capacity " + count);
                if (capacity != null && capacity.Length >= count) Check(ReferenceEquals(capacity, current), "VM reallocated sufficient capacity");
                capacity = current;
            }
            capacity[0] = 91;
            Set("byteCount", 8);
            Set("fieldIndex", 1);
            Call("CopyField");
            capacity[0] = 45;
            Set("fieldIndex", 0);
            Call("CopyField");
            Set("byteCount", 0);
            Call("CopyField");
            Check(Get<byte[]>("first").Length == 0 && Get<byte[]>("second")[0] == 91, "VM field ownership");
            AssetDatabase.SaveAssets();
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), "PASS\nFull client Udon compile and actual helper bytecode: empty/shrink/grow/max, capacity identity and independent retained fields\n");
        }
        catch (Exception error)
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), "FAIL\n" + error);
            throw;
        }
    }
#endif

    public static void Measure()
    {
        var results = new List<string> { "PASS", "Unity=" + Application.unityVersion, "10000 alternating 2048/2065-byte requests after warm-up; helper allocations, not whole-frame GC" };
        foreach (bool copy in new[] { false, true })
        {
            for (int i = 0; i < 100; i++) Step(i, copy);
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                Check(recorder.Valid, "GC.Alloc recorder unavailable");
                int replacements = 0;
                long allocatedPayloadBytes = 0;
                for (int i = 0; i < 10000; i++)
                {
                    byte[] previous = buffer;
                    Step(i, copy);
                    if (!ReferenceEquals(previous, buffer))
                    {
                        replacements++;
                        allocatedPayloadBytes += buffer.Length;
                    }
                }
                recorder.Stop();
                long allocations = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                Check(replacements == 0 || allocations >= replacements, "GC recorder missed allocations");
                results.Add((copy ? "CopyPrefix" : "EnsureByteBuffer") + " GC.Alloc count=" + allocations + "; replacements=" + replacements + "; allocated array payload bytes (excluding object headers)=" + allocatedPayloadBytes);
            }
        }
        File.WriteAllLines(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), results);
    }

    static void Step(int i, bool copy)
    {
        int count = 2048 + (i % 2) * 17;
        buffer = copy ? NetworkPayloadBuffer.CopyPrefix(Source, count, buffer) : DecoderReadbackRuntime.EnsureByteBuffer(buffer, count);
    }

    public static void Run()
    {
        var root = new GameObject("Payload buffer validation");
        try
        {
#if UDONSHARP
            var decoder = root.AddUdonSharpComponent<TSMPDecoder>();
#else
            var decoder = root.AddComponent<TSMPDecoder>();
#endif
            decoder.applyEveryFrame = false;
            decoder.decodeSafetyMode = 2;
            byte[] capacity = DecoderReadbackRuntime.EnsureByteBuffer(null, 65535);
            foreach (int count in new[] { 0, 1, 7, 8, 65535, 32, 65534, 8 })
                Check(ReferenceEquals(capacity, DecoderReadbackRuntime.EnsureByteBuffer(capacity, count)), "High-water capacity reuse " + count);
            Check(DecoderReadbackRuntime.EnsureByteBuffer(null, 0).Length == 0, "Initial empty buffer");
            Check(DecoderReadbackRuntime.EnsureByteBuffer(new byte[2], 3).Length >= 3, "Growth");
            int cursor = NetworkFrameWriter.BeginNetworkFrame(capacity, 0, 1);
            NetworkFrameWriter.EndNetworkFrame(capacity, 0, 0);
            for (int i = cursor; i < capacity.Length; i++) capacity[i] = 255;
            Set(decoder, "_payloadBytes", capacity);
            Set(decoder, "_payloadDataBytes", cursor);
            Check(Apply(decoder), "Valid empty NetworkFrame with dirty capacity tail");
            Check(decoder.lastPayloadAvailableBytes == cursor, "Diagnostics report valid bytes, not capacity");
            Set(decoder, "_payloadDataBytes", 0);
            Check(!Apply(decoder), "Zero bytes must not read the old frame");
            Set(decoder, "_payloadDataBytes", 7);
            Check(!Apply(decoder), "Truncated header must not use old capacity bytes");
            Set(decoder, "_payloadDataBytes", 65536);
            Check(!Apply(decoder), "Reject valid length larger than capacity");
            Set(decoder, "_payloadDataBytes", cursor + 1);
            Check(!Apply(decoder), "Reject trailing bytes inside valid length");
            int start = cursor;
            int body = NetworkFrameWriter.BeginVariableState(capacity, start, 1, 0);
            NetworkFrameWriter.EndVariableState(capacity, start, body, 0);
            NetworkFrameWriter.EndNetworkFrame(capacity, 0, 1);
            Set(decoder, "_payloadDataBytes", body - 1);
            Check(!Apply(decoder), "Message body cannot extend into retained capacity");
            Set(decoder, "_payloadDataBytes", body);
            Check(Apply(decoder), "Complete message accepted");

            byte[] first = NetworkValueReader.CopyRawBytes(capacity, 0, 8, null);
            byte[] second = NetworkValueReader.CopyRawBytes(capacity, 8, 8, null);
            byte[] preserved = (byte[])second.Clone();
            Array.Clear(capacity, 0, capacity.Length);
            first = NetworkValueReader.CopyRawBytes(capacity, 0, 4, first);
            Check(!ReferenceEquals(first, second) && preserved.SequenceEqual(second), "Received field arrays remain isolated from input and each other");

            DecoderHeaderRuntime.ResolvePayloadLayout(true, 640, 8, 80, 1, 0, 5, 8, 1, 80, 4096, capacity,
                out _, out _, out _, out int valid, out byte[] retained, out _, out _);
            Check(valid == 0 && ReferenceEquals(capacity, retained), "Zero header payload clears valid length without discarding capacity");
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), "PASS\nCapacity reuse, empty/shrink/grow/max, bounded parsing, dirty tails, independent raw fields, zero header payload\n");
        }
        catch (Exception error)
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), "FAIL\n" + error);
            throw;
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void Set(TSMPDecoder decoder, string name, object value) => typeof(TSMPDecoder).GetField(name, Private).SetValue(decoder, value);
    static bool Apply(TSMPDecoder decoder) => (bool)typeof(TSMPDecoder).GetMethod("ApplyNetworkFrame", Private).Invoke(decoder, null);
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
