using System;
using System.IO;
using UnityEditor;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
using VRC.Udon;
#endif

// Request-only regression runner; no user menu and no scene saving.
[InitializeOnLoad]
public static class DecoderDispatchValidation
{
    const string RootName = "__TSMP_DispatchValidation";
    const string Request = "Temp/DispatchGuard.request";
    const string Result = "Temp/DispatchGuard.result.json";
    [Serializable] public class Outcome
    {
        public string project, action, error, failureDetails;
        public int checks, failures, benchmarkApplied, iterations;
        public double benchmarkMs;
        public bool playing, udon;
    }
    static DecoderDispatchValidation() { EditorApplication.update += Poll; }
    static DecoderDispatchProbe Add(GameObject go)
    {
#if UDONSHARP
        return go.AddUdonSharpComponent<DecoderDispatchProbe>();
#else
        return go.AddComponent<DecoderDispatchProbe>();
#endif
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        string action = File.ReadAllText(Request).Trim(); File.Delete(Request);
        var result = new Outcome { project = Application.dataPath, action = action, playing = Application.isPlaying };
        try
        {
            var root = GameObject.Find(RootName);
            if (action == "prepare")
            {
                if (Application.isPlaying || root != null) throw new InvalidOperationException("Prepare requires Edit mode and no previous fixture.");
                root = new GameObject(RootName);
                var a = new GameObject("Dispatch target A"); a.transform.SetParent(root.transform);
                var b = new GameObject("Dispatch target B"); b.transform.SetParent(root.transform);
                var driver = Add(root); var first = Add(a); var second = Add(b);
#if UDONSHARP
                driver.targetA = UdonSharpEditorUtility.GetBackingUdonBehaviour(first);
                driver.targetB = UdonSharpEditorUtility.GetBackingUdonBehaviour(second);
                UdonSharpEditorUtility.CopyProxyToUdon(driver);
                UdonSharpEditorUtility.CopyProxyToUdon(first);
                UdonSharpEditorUtility.CopyProxyToUdon(second);
#else
                driver.targetA = first; driver.targetB = second;
#endif
            }
            else if (action == "cleanup")
            {
                if (Application.isPlaying) throw new InvalidOperationException("Cleanup requires Edit mode.");
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
            else
            {
                if (root == null) throw new InvalidOperationException("Fixture not prepared.");
                var driver = root.GetComponent<DecoderDispatchProbe>();
                string entry = action == "checks" ? "RunChecks" : action == "benchmark" ? "RunBenchmark" : throw new ArgumentException(action);
#if UDONSHARP
                if (!Application.isPlaying) throw new InvalidOperationException("VRC validation must run actual Udon in Play mode.");
                var udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(driver);
                udon.SendCustomEvent(entry);
                result.udon = true;
                result.checks = (int)udon.GetProgramVariable("checks");
                result.failures = (int)udon.GetProgramVariable("failures");
                result.failureDetails = (string)udon.GetProgramVariable("failureDetails");
                result.benchmarkApplied = (int)udon.GetProgramVariable("benchmarkApplied");
                result.iterations = (int)udon.GetProgramVariable("benchmarkIterations");
                result.benchmarkMs = (double)udon.GetProgramVariable("benchmarkSeconds") * 1000;
#else
                if (action == "checks") driver.RunChecks(); else driver.RunBenchmark();
                result.checks = driver.checks; result.failures = driver.failures; result.failureDetails = driver.failureDetails;
                result.benchmarkApplied = driver.benchmarkApplied; result.iterations = driver.benchmarkIterations;
                result.benchmarkMs = driver.benchmarkSeconds * 1000;
#endif
                if (action == "checks" && (result.checks != 21 || result.failures != 0)) throw new Exception("Regression failure: " + result.failureDetails);
                if (action == "benchmark" && result.benchmarkApplied != result.iterations * 2) throw new Exception("Benchmark did not apply all values.");
            }
        }
        catch (Exception e) { result.error = e.ToString(); }
        File.WriteAllText(Result, JsonUtility.ToJson(result, true));
    }
}
