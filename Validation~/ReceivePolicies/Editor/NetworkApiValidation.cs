#if UDONSHARP
using System;
using System.IO;
using System.Linq;
using VRC.Udon.Editor;

public static class NetworkApiValidation
{
    public static void Run()
    {
        var names = UdonEditorManager.Instance.GetNodeDefinitions().Select(node => node.fullName)
            .Where(name => name.Contains("SkinnedMeshRenderer") || name.Contains("UnityEngineMesh") || name.Contains("AnimatorStateInfo"))
            .OrderBy(name => name);
        File.WriteAllLines(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), names);
    }
}
#endif
