#if !COMPILER_UDONSHARP
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
#endif
using Object = UnityEngine.Object;

public sealed class ContinuousFrameValidation : MonoBehaviour
{
    public TSMPCodec[] codecPrefabs;
    public Material expandMaterial;
    public static Func<ContinuousFrameValidation, Case, ILoopback> UdonFactory;
    public const int Capacity = 32768;
    public const string CsvHeader = "case,path,codec,width,height,valueBytes,payloadBytes,sample,sendTarget,loopTarget,seconds,loopHz,published,txHz,appliedInWindow,applyHz,receivedAfterDrain,missPercent,schedulerMisses,captureCount,busyPercent,encodeP95ms,latencyP50ms,latencyP95ms,latencyP99ms,latencyMaxMs,snapshotP95ms,applyGapP95ms,applyGapMaxMs,latencyFirstThirdMs,latencyLastThirdMs,sourceAgeP95ms,decoderErrorObservations,corrupt,outOfOrder";

    public sealed class Case
    {
        public string Name;
        public int Codec;
        public int Width = 640;
        public int Height = 360;
        public int Bytes = 32;
        public int Sample = 1;
        public int SendHz = 60;
        public int LoopHz = 60;
        public double Seconds = 12;
        public bool Baseline;
    }

    public interface ILoopback : IDisposable
    {
        bool Busy { get; }
        int PayloadBytes { get; }
        int AppliedCount { get; }
        int CorruptCount { get; }
        int[] AppliedIds { get; }
        float[] AppliedTimes { get; }
        string Error { get; }
        string DecoderError { get; }
        string Diagnostics { get; }
        void Publish(byte[] packet);
        void Decode();
        void Stop();
    }

    readonly List<string> rows = new List<string>();
    readonly double[] published = new double[Capacity];
    readonly double[] captureTimes = new double[Capacity];
    readonly double[] sent = new double[Capacity];
    string resultRoot;
    string failure;

#if UNITY_EDITOR
    public static void CreateScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var runner = new GameObject("Continuous frame validation").AddComponent<ContinuousFrameValidation>();
        runner.codecPrefabs = new[] { "Luma4", "RGB16", "RGB20", "Color256" }.Select(name =>
            AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.kibalab.tsmp.codec." + name.ToLowerInvariant() +
                "/Runtime/Codec_" + name + ".prefab").GetComponent<TSMPCodec>()).ToArray();
        runner.expandMaterial = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.core/Shaders/TSMPEncoderBlockExpand.mat");
        new GameObject("Camera").AddComponent<Camera>().cullingMask = 0;
        PlayerSettings.colorSpace = ColorSpace.Gamma;
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/ContinuousFrames/Validation.unity");
    }

    public static void Play()
    {
        CreateScene();
        EditorApplication.EnterPlaymode();
    }

    public static void Build()
    {
        CreateScene();
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        string path = Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_BUILD");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/ContinuousFrames/Validation.unity" }, locationPathName = path,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        File.WriteAllText(Path.ChangeExtension(path, ".build-report.txt"), "Result=" + report.summary.result +
            "\nErrors=" + report.summary.totalErrors + "\nWarnings=" + report.summary.totalWarnings + "\nBackend=Mono\nStripping=Disabled");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
#endif

    IEnumerator Start()
    {
        resultRoot = Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_RESULTS");
        Directory.CreateDirectory(resultRoot);
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.logMessageReceived += OnLog;
        File.WriteAllText(Path.Combine(resultRoot, "environment.txt"), "Unity=" + Application.unityVersion +
            "\nGPU=" + SystemInfo.graphicsDeviceName + "\nAPI=" + SystemInfo.graphicsDeviceType +
            "\nColorSpace=" + QualitySettings.activeColorSpace + "\nEditor=" + Application.isEditor +
            "\nPath=" + (UdonFactory == null ? "Native" : "Compiled Udon VM") +
            "\nManaged source=" + Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_REVISION"));
        rows.Add(CsvHeader);
        var work = Run();
        while (true)
        {
            bool next;
            try
            {
                if (failure != null) throw new InvalidOperationException(failure);
                next = work.MoveNext();
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(resultRoot, "status.txt"), "FAIL\n" + error);
                Finish(1);
                yield break;
            }
            if (!next) break;
            yield return work.Current;
        }
        File.WriteAllText(Path.Combine(resultRoot, "status.txt"), "PASS: measurements completed; this does not assert lossless delivery.");
        Finish(0);
    }

    void OnLog(string text, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) failure = text + "\n" + stack;
    }

    void Finish(int code)
    {
        Application.logMessageReceived -= OnLog;
#if UNITY_EDITOR
        EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    static IEnumerable<Case> Cases()
    {
        yield return new Case { Name = "sender-only-60", Baseline = true };
        foreach (int loop in new[] { 60, 90, 120, 144, 180, 240, -1 })
            yield return new Case { Name = "small-60-at-" + loop, LoopHz = loop };
        foreach (int send in new[] { 15, 30 })
            yield return new Case { Name = "small-" + send + "-at-60", SendHz = send };
        foreach (int send in new[] { 30, 90, 120 })
            yield return new Case { Name = "small-" + send + "-at-120", LoopHz = 120, SendHz = send };
        yield return new Case { Name = "large-60-at-60", Bytes = 1024 };
        yield return new Case { Name = "large-60-at-120", Bytes = 1024, LoopHz = 120 };
        for (int codec = 0; codec < 4; codec++)
            yield return new Case { Name = "codec-" + codec + "-sample4", Codec = codec, Bytes = 1024, Sample = 4, LoopHz = 120 };
        yield return new Case { Name = "hd-small", Width = 1280, Height = 720, LoopHz = 120 };
        yield return new Case { Name = "hd-large", Width = 1280, Height = 720, Bytes = 4096, LoopHz = 120 };
        yield return new Case { Name = "sustained-60", Seconds = 60 };
    }

    IEnumerator Run()
    {
        string filter = Environment.GetEnvironmentVariable("TSMP_CONTINUOUS_FILTER");
        foreach (Case test in Cases())
        {
            if (!string.IsNullOrEmpty(filter) && !filter.Split(',').Contains(test.Name)) continue;
            Application.targetFrameRate = test.LoopHz;
            using (ILoopback loop = UdonFactory != null ? UdonFactory(this, test) : CreateNative(test))
            {
                yield return null;
                var run = Measure(test, loop);
                while (run.MoveNext()) yield return run.Current;
            }
            yield return null;
            GC.Collect();
        }
    }

    IEnumerator Measure(Case test, ILoopback loop)
    {
        Array.Clear(published, 0, published.Length);
        Array.Clear(sent, 0, sent.Length);
        Array.Clear(captureTimes, 0, captureTimes.Length);
        var packet = new byte[test.Bytes];
        for (int i = 0; i < packet.Length; i++) packet[i] = (byte)(i * 37);
        SetPacketId(packet, 1);
        sent[1] = Time.realtimeSinceStartupAsDouble;
        loop.Publish(packet);
        published[1] = Time.realtimeSinceStartupAsDouble;
        if (!test.Baseline)
        {
            captureTimes[1] = Time.realtimeSinceStartupAsDouble;
            loop.Decode();
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (loop.Busy && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (loop.Busy || loop.AppliedCount != 1 || loop.AppliedIds[0] != 1)
                throw new InvalidOperationException("Preflight failed: " + loop.Diagnostics);
        }
        double begin = Time.realtimeSinceStartupAsDouble + 2;
        double end = begin + test.Seconds;
        double due = Time.realtimeSinceStartupAsDouble;
        double interval = 1.0 / test.SendHz;
        int id = 1, sends = 0, updates = 0, busy = 0, captures = 0, missed = 0, decodeErrors = 0;
        var encodes = new List<double>();
        var ages = new List<double>();
        int latestApplied = 0;
        while (Time.realtimeSinceStartupAsDouble < end + 1)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            bool measured = now >= begin && now < end;
            if (measured) updates++;
            if (now >= due)
            {
                int elapsedSlots = Math.Max(1, (int)Math.Floor((now - due) / interval) + 1);
                if (measured) missed += elapsedSlots - 1;
                due += elapsedSlots * interval;
                id++;
                if (id >= Capacity) throw new InvalidOperationException("Measurement capacity exceeded");
                SetPacketId(packet, id);
                sent[id] = Time.realtimeSinceStartupAsDouble;
                loop.Publish(packet);
                published[id] = Time.realtimeSinceStartupAsDouble;
                if (sent[id] >= begin && sent[id] < end)
                {
                    sends++;
                    encodes.Add((published[id] - sent[id]) * 1000);
                }
            }

            if (!test.Baseline)
            {
                if (measured && !string.IsNullOrEmpty(loop.DecoderError)) decodeErrors++;
                if (loop.Busy)
                {
                    if (measured) busy++;
                }
                else
                {
                    if (captureTimes[id] == 0) captureTimes[id] = Time.realtimeSinceStartupAsDouble;
                    if (measured) captures++;
                }
                loop.Decode();
                int count = loop.AppliedCount;
                if (count > 0) latestApplied = loop.AppliedIds[count - 1];
                if (measured && latestApplied > 0) ages.Add((Time.realtimeSinceStartupAsDouble - sent[latestApplied]) * 1000);
            }
            yield return null;
        }

        double stop = Time.realtimeSinceStartupAsDouble;
        while (loop.Busy && Time.realtimeSinceStartupAsDouble < stop + 5) yield return null;
        if (loop.Busy) throw new InvalidOperationException("Readback did not complete after sender stopped");
        if (!string.IsNullOrEmpty(loop.Error)) throw new InvalidOperationException(loop.Error);

        var latencies = new List<double>();
        var snapshotLatency = new List<double>();
        var gaps = new List<double>();
        var first = new List<double>();
        var last = new List<double>();
        int received = 0, inWindow = 0, outOfOrder = 0, previous = 0;
        double previousTime = 0;
        var unique = new HashSet<int>();
        var trace = new List<string> { "id,sentSeconds,publishedSeconds,capturedSeconds,appliedSeconds,latencyMs,snapshotMs,inMeasuredWindow" };
        int countFinal = loop.AppliedCount;
        int[] ids = loop.AppliedIds;
        float[] times = loop.AppliedTimes;
        for (int i = 0; i < countFinal; i++)
        {
            int applied = ids[i];
            if (applied <= previous || applied > id) outOfOrder++;
            previous = applied;
            if (applied <= 0 || applied >= Capacity || sent[applied] <= 0) continue;
            double stamp = times[i];
            double delay = (stamp - sent[applied]) * 1000;
            bool included = sent[applied] >= begin && sent[applied] < end;
            trace.Add(string.Join(",", applied, N(sent[applied]), N(published[applied]), N(captureTimes[applied]), N(stamp), N(delay), N((stamp - captureTimes[applied]) * 1000), included));
            if (!included) continue;
            if (!unique.Add(applied)) throw new InvalidOperationException("Duplicate actual application");
            received++;
            if (stamp < end) inWindow++;
            latencies.Add(delay);
            if (captureTimes[applied] > 0) snapshotLatency.Add((stamp - captureTimes[applied]) * 1000);
            if (previousTime > 0) gaps.Add((stamp - previousTime) * 1000);
            previousTime = stamp;
            if (sent[applied] < begin + test.Seconds / 3) first.Add(delay);
            if (sent[applied] >= end - test.Seconds / 3) last.Add(delay);
        }
        if (!test.Baseline && (received == 0 || outOfOrder != 0 || loop.CorruptCount != 0))
            throw new InvalidOperationException("Invalid application results: received=" + received + ", corrupt=" + loop.CorruptCount + ", order=" + outOfOrder + "; " + loop.Diagnostics);
        string row = string.Join(",", test.Name, UdonFactory == null ? "Native" : "UdonVM", codecPrefabs[test.Codec].name,
            test.Width, test.Height, test.Bytes, loop.PayloadBytes, test.Sample, test.SendHz, test.LoopHz, N(test.Seconds),
            N(updates / test.Seconds), sends, N(sends / test.Seconds), inWindow, N(inWindow / test.Seconds), received,
            N(sends == 0 ? 0 : 100.0 * (sends - received) / sends), missed, captures, N(updates == 0 ? 0 : 100.0 * busy / updates),
            N(P(encodes, .95)), N(P(latencies, .5)), N(P(latencies, .95)), N(P(latencies, .99)), N(P(latencies, 1)),
            N(P(snapshotLatency, .95)), N(P(gaps, .95)), N(P(gaps, 1)), N(Average(first)), N(Average(last)), N(P(ages, .95)),
            decodeErrors, loop.CorruptCount, outOfOrder);
        rows.Add(row);
        File.WriteAllLines(Path.Combine(resultRoot, "summary.csv"), rows);
        File.WriteAllLines(Path.Combine(resultRoot, test.Name + "-applications.csv"), trace);
        var publications = new List<string> { "id,sentSeconds,publishedSeconds,capturedSeconds,inMeasuredWindow" };
        for (int i = 1; i <= id; i++)
            publications.Add(string.Join(",", i, N(sent[i]), N(published[i]), N(captureTimes[i]), sent[i] >= begin && sent[i] < end));
        File.WriteAllLines(Path.Combine(resultRoot, test.Name + "-publications.csv"), publications);
        File.WriteAllText(Path.Combine(resultRoot, "progress.txt"), row);
        Debug.Log("CONTINUOUS " + row);
        loop.Stop();
    }

    static double Average(List<double> values) => values.Count == 0 ? 0 : values.Average();
    static void SetPacketId(byte[] packet, int id)
    {
        packet[0] = (byte)(id & 255);
        packet[1] = (byte)((id >> 8) & 255);
        packet[2] = (byte)((id >> 16) & 255);
        for (int i = 0; i < 3; i++) packet[packet.Length - 4 + i] = (byte)(255 - packet[i]);
    }
    static double P(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(percentile * sorted.Length) - 1)];
    }
    static string N(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    public static RenderTexture Texture(int width, int height)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        if (!texture.Create()) throw new InvalidOperationException("RenderTexture allocation failed");
        return texture;
    }

    public TSMPCodec CloneCodec(int index, List<Object> owned)
    {
        TSMPCodec codec = Instantiate(codecPrefabs[index].gameObject).GetComponent<TSMPCodec>();
        owned.Add(codec.gameObject);
        var copies = new Dictionary<Material, Material>();
        Func<Material, Material> copy = material =>
        {
            if (material == null) return null;
            if (!copies.TryGetValue(material, out Material clone))
            {
                clone = new Material(material);
                copies.Add(material, clone);
                owned.Add(clone);
            }
            return clone;
        };
        foreach (var field in codec.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (field.FieldType == typeof(Material)) field.SetValue(codec, copy((Material)field.GetValue(codec)));
            if (field.FieldType == typeof(Material[]) && field.GetValue(codec) is Material[] array)
                field.SetValue(codec, array.Select(copy).ToArray());
        }
        return codec;
    }

    static ILoopback CreateNative(Case test)
    {
#if !UDONSHARP
        return new NativeLoopback(FindObjectOfType<ContinuousFrameValidation>(), test);
#else
        throw new InvalidOperationException("SDK tests must install the compiled Udon adapter");
#endif
    }

#if !UDONSHARP
    sealed class NativeLoopback : ILoopback
    {
        readonly List<Object> owned = new List<Object>();
        readonly TSMPEncoder encoder;
        readonly TSMPDecoder decoder;
        readonly ContinuousFrameProbe sender;
        readonly ContinuousFrameProbe receiver;
        public NativeLoopback(ContinuousFrameValidation runner, Case test)
        {
            var root = new GameObject("Continuous native loopback");
            root.SetActive(false);
            owned.Add(root);
            sender = root.AddComponent<ContinuousFrameProbe>();
            receiver = root.AddComponent<ContinuousFrameProbe>();
            sender.networkId = receiver.networkId = 1;
            receiver.appliedIds = new int[Capacity];
            receiver.appliedTimes = new float[Capacity];
            TSMPCodec codec = runner.CloneCodec(test.Codec, owned);
            TSMPCodec luma = test.Codec == 0 ? codec : runner.CloneCodec(0, owned);
            encoder = root.AddComponent<TSMPEncoder>();
            encoder.autoEncode = false;
            encoder.output = Texture(test.Width, test.Height);
            owned.Add(encoder.output);
            encoder.sampleSize = test.Sample;
            encoder.frameIndex = 1;
            encoder.selectedCodec = codec;
            encoder.networkBehaviours = new TSMPNetworkBehaviour[] { sender };
            encoder.debugLog = false;
            decoder = root.AddComponent<TSMPDecoder>();
            decoder.applyEveryFrame = false;
            decoder.sourceTexture = encoder.output;
            decoder.payloadByteTexture = Texture(512, Math.Max(1, (test.Bytes + 64 + 2047) / 2048));
            owned.Add(decoder.payloadByteTexture);
            decoder.codecHandlers = test.Codec == 0 ? new[] { luma } : new[] { luma, codec };
            decoder.flipY = true;
            decoder.sampleSize = test.Sample;
            decoder.debugLog = true;
            var field = TransSyncMetadata.GetOrCreate(null, typeof(ContinuousFrameProbe)).Fields.Single();
            decoder.bindingTargets = new Component[] { receiver };
            decoder.bindingNetworkIds = new ushort[] { 1 };
            decoder.bindingVariableHashes = new[] { field.VariableHash };
            decoder.bindingValueTypes = new[] { (byte)field.ValueType };
            decoder.bindingFieldNames = new[] { nameof(ContinuousFrameProbe.packet) };
            decoder.bindingDirections = new[] { 0 };
            root.SetActive(true);
        }
        public bool Busy => decoder.readbackInFlight;
        public int PayloadBytes => encoder.payloadBytes;
        public int AppliedCount => receiver.appliedCount;
        public int CorruptCount => receiver.corruptCount;
        public int[] AppliedIds => receiver.appliedIds;
        public float[] AppliedTimes => receiver.appliedTimes;
        public string Error => encoder.lastError;
        public string DecoderError => decoder.lastFrameValid ? null : decoder.lastError;
        public string Diagnostics => "header=" + decoder.lastHeaderValid + ", valid=" + decoder.lastFrameValid + ", frame=" + decoder.lastFrameIndex + ", messages=" + decoder.lastNetworkMessageCount + ", applied=" + decoder.lastAppliedVariableCount + ", error=" + decoder.lastError + ", receiverId=" + receiver.networkId + ", senderId=" + sender.networkId;
        public void Publish(byte[] packet)
        {
            sender.packet = packet;
            uint before = encoder.frameIndex;
            encoder.EncodeNow();
            if (encoder.frameIndex != before + 1) throw new InvalidOperationException("Encoder output failure: " + encoder.lastError);
        }
        public void Decode() => decoder.DecodeNow();
        public void Stop() => decoder.enabled = false;
        public void Dispose()
        {
            RenderTexture.active = null;
            foreach (var item in owned)
            {
                if (item is RenderTexture texture) texture.Release();
                if (item != null) Destroy(item);
            }
        }
    }
#endif
}
#endif
