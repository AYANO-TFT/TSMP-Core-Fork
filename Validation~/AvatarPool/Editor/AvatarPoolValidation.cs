using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
#endif
using Object = UnityEngine.Object;

public static class AvatarPoolValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void Run()
    {
        var results = new List<string>();
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (bool supplied in new[] { false, true }) Test(supplied, results);
            File.WriteAllLines(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), new[] { "PASS", Application.unityVersion }.Concat(results));
        }
        catch (Exception error)
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT"), "FAIL\n" + error);
            throw;
        }
    }

    static void Test(bool supplied, List<string> results)
    {
        var root = new GameObject("Pool validation");
        var prefab = new GameObject("Avatar template");
        try
        {
#if UDONSHARP
            var sync = root.AddUdonSharpComponent<TSMPNetworkVrchatAvatarPoseSync>();
            prefab.AddUdonSharpComponent<TSMPNetworkVrchatAvatarPoseRig>();
#else
            var sync = root.AddComponent<TSMPNetworkVrchatAvatarPoseSync>();
            prefab.AddComponent<TSMPNetworkVrchatAvatarPoseRig>();
#endif
            sync.maxPlayers = 3;
            sync.avatarPrefab = prefab;
            sync.avatarPoolRoot = root.transform;
            if (supplied)
                sync.avatarPool = Enumerable.Range(0, 3).Select(_ => Object.Instantiate(prefab, root.transform)).ToArray();
            Apply(sync, 1, 2, 3);
            var original = sync.avatarPool.ToArray();
            var rigs = sync.avatarRigs.ToArray();
            Check(sync.activeAvatarCount == 3 && sync.poolSize == 3, "Initial population");
            for (int cycle = 0; cycle < 8; cycle++)
            {
                sync.maxPlayers = 1;
                typeof(TSMPNetworkVrchatAvatarPoseSync).GetMethod("RetireStaleAvatars", Private).Invoke(sync, null);
                Check(sync.activeAvatarCount == 1 && sync.poolSize == 3, "Shrink without an incoming packet");
                Check(original[0].activeSelf && !original[1].activeSelf && !original[2].activeSelf, "Retire overflow objects");
                Check(sync.avatarPool.SequenceEqual(original) && sync.avatarRigs.SequenceEqual(rigs), "Retain pool and rig references");
                Apply(sync, 1, 200, 201);
                Check(sync.failedAvatarSlotCount == 2 && sync.activeAvatarCount == 1, "No activation above limit");
                sync.maxPlayers = 3;
                Apply(sync, 1, 200 + cycle * 2, 201 + cycle * 2);
                Check(sync.lastAvatarPoseError == 0 && sync.activeAvatarCount == 3, "Rejoin with new IDs");
                Check(root.transform.childCount == 3 && sync.avatarPool.SequenceEqual(original), "Reuse instead of duplicating");
            }
            sync.maxPlayers = 0;
            Apply(sync, 1);
            Check(sync.maxPlayers == 1 && sync.activeAvatarCount == 1, "Clamp zero capacity");
            sync.maxPlayers = 100;
            Apply(sync, 1, 2, 3);
            Check(sync.maxPlayers == 80 && sync.poolSize == 3 && sync.avatarPool.Length == 80, "Clamp and grow capacity");
            results.Add("PASS " + (supplied ? "supplied" : "generated") + ": shrink, idle resize, eight regrow/rejoin cycles, references, counters, capacity clamps");
        }
        finally { Object.DestroyImmediate(root); Object.DestroyImmediate(prefab); }
    }

    static void Apply(TSMPNetworkVrchatAvatarPoseSync sync, params int[] ids)
    {
        byte[] packet = new byte[6 + ids.Length * 5];
        VrchatAvatarPosePacket.WriteHeader(packet, 4, 2, ids.Length, 0, 0);
        int cursor = 6;
        foreach (int id in ids) cursor = VrchatAvatarPosePacket.WritePlayerHeader(packet, cursor, id, 0, 0, false);
        sync.avatarPoseBytes = packet;
        sync.OnTSMPVariableReceived();
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
