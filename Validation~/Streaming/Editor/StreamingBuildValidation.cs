using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreamingBuildValidation
{
    public static void Build()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Streaming Validation").AddComponent<StreamingValidationRunner>();
        new GameObject("Camera").AddComponent<Camera>();
        const string scene = "Assets/Validation/Streaming/Streaming.unity";
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scene);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Disabled);
        string path = Environment.GetEnvironmentVariable("TSMP_VALIDATION_BUILD");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { scene }, locationPathName = path,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
        });
        string result = "Result=" + report.summary.result + "\nErrors=" + report.summary.totalErrors +
            "\nWarnings=" + report.summary.totalWarnings + "\nTarget=Windows x64\nBackend=Mono\nStripping=Disabled";
        File.WriteAllText(Path.ChangeExtension(path, ".build-report.txt"), result);
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"),
            (report.summary.result == BuildResult.Succeeded ? "PASS\n" : "FAIL\n") + result);
        if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(result);
    }
}
