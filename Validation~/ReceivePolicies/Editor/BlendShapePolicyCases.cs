using System;
using K13A.TSMP.Udon;
using UnityEngine;
using Endpoint = ReceivePolicyValidation.Endpoint;
using Object = UnityEngine.Object;

public static class BlendShapePolicyCases
{
    public static readonly string[] Changes = { "selection", "null selection", "renderer", "mesh", "smaller mesh", "empty mesh", "None", "Discrete", "disable", "inactive" };

    private static Mesh CreateMesh(int count)
    {
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        for (int i = 0; i < count; i++)
            mesh.AddBlendShapeFrame("Shape" + i, 100, new[] { Vector3.up, Vector3.up, Vector3.up }, new Vector3[3], new Vector3[3]);
        return mesh;
    }

    private static void Tick(Endpoint sync)
    {
#if UDONSHARP
        sync.Call("_postLateUpdate");
#else
        sync.Call("LateUpdate");
#endif
    }

    public static void Run(string change)
    {
        using (var sync = new Endpoint(typeof(TSMPNetworkBlendShapesSync)))
        {
            var owner = new GameObject("BlendShape renderer");
            var replacement = new GameObject("Replacement renderer");
            Mesh mesh = CreateMesh(2);
            Mesh otherMesh = CreateMesh(change == "smaller mesh" ? 1 : change == "empty mesh" ? 0 : 2);
            try
            {
                var renderer = owner.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                sync.Set("targetRenderer", renderer);
                sync.Set("blendShapeCount", 2);
                sync.Set("blendShapeIndices", new[] { 0, 1 });
                sync.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
                sync.Set("continuousInterpolationRate", 0f);
                sync.Set("blendShapeBytes", new byte[] { 1, 2, 0, 0, 50, 1, 0, 80 });
                sync.Call("OnTSMPVariableReceived");
                Check(renderer.GetBlendShapeWeight(0) == 0, "Continuous applied before its update");
                if (change == "selection") sync.Get<int[]>("blendShapeIndices")[0] = -1;
                else if (change == "null selection") sync.Set("blendShapeIndices", null);
                else if (change == "renderer")
                {
                    renderer = replacement.AddComponent<SkinnedMeshRenderer>();
                    renderer.sharedMesh = mesh;
                    sync.Set("targetRenderer", renderer);
                }
                else if (change.Contains("mesh")) renderer.sharedMesh = otherMesh;
                else if (change == "None" || change == "Discrete")
                    sync.Set("receiveInterpolation", change == "None" ? ReceiveInterpolationMode.None : ReceiveInterpolationMode.Discrete);
                else
                {
                    if (change == "inactive") sync.Target.gameObject.SetActive(false);
                    else ((Behaviour)sync.Target).enabled = false;
#if UDONSHARP
                    sync.Call("_onDisable");
#else
                    sync.Call("OnDisable");
#endif
                }
                Tick(sync);
                if (renderer.sharedMesh.blendShapeCount > 0)
                    Check(renderer.GetBlendShapeWeight(0) == 0, "Old target updated a removed/replaced/disabled shape");
                if (change == "selection") Check(renderer.GetBlendShapeWeight(1) == 80, "Selected target stopped interpolating");
                sync.Target.gameObject.SetActive(true);
                ((Behaviour)sync.Target).enabled = true;
                sync.Set("receiveInterpolation", ReceiveInterpolationMode.Continuous);
                sync.Set("blendShapeIndices", new[] { 0, 1 });
                Tick(sync);
                if (renderer.sharedMesh.blendShapeCount > 0)
                    Check(renderer.GetBlendShapeWeight(0) == 0, "Restored selection/context replayed an old target");
                sync.Set("receiveInterpolation", ReceiveInterpolationMode.Discrete);
                sync.Call("OnTSMPVariableReceived");
                if (renderer.sharedMesh.blendShapeCount > 0)
                {
                    Check(renderer.GetBlendShapeWeight(0) == 50, "New packet did not apply");
                    renderer.SetBlendShapeWeight(0, 0);
                    sync.Call("OnTSMPVariableReceived");
                    Check(renderer.GetBlendShapeWeight(0) == 50, "Repeated Discrete packet did not restore externally modified value");
                }
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(replacement);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(otherMesh);
            }
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
