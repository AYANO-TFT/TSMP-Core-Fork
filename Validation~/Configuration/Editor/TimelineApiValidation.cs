using System;
using System.IO;
using System.Linq;
using UnityEngine;
#if UDONSHARP
using VRC.Udon.Editor;
#endif

public static class TimelineApiValidation
{
    public static void Run()
    {
#if UDONSHARP
        var names = UdonEditorManager.Instance.GetNodeDefinitions().Select(node => node.fullName)
            .Where(name => name.Contains("PlayableDirector") || name.Contains("PlayableGraph"))
            .OrderBy(name => name);
        File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), "PASS\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", names));
#endif
    }
}
