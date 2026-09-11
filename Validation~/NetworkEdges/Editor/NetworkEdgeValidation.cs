using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
#endif
using Object = UnityEngine.Object;

public static class NetworkEdgeValidation
{
    private static readonly List<string> Results = new List<string>();
    private static bool failed;

    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Test("Blendshape external overwrite", BlendshapeOverwrite);
        Test("Animator 256 parameters", () => AnimatorPacket(256, 0, false));
        Test("Animator 256 layers", () => AnimatorPacket(0, 256, false));
        Test("Animator mixed entries and limits", () => AnimatorPacket(300, 300, true));
        Test("Animator 254 entries", () => AnimatorPacket(254, 254, false));
        Test("Animator 255 entries", () => AnimatorPacket(255, 255, false));
        Test("Animator empty selection", () => AnimatorPacket(0, 0, false));
        Test("Animator Inspector limits", InspectorLimits);
        string output = Environment.GetEnvironmentVariable("TSMP_VALIDATION_RESULT");
        File.WriteAllText(output, (failed ? "FAIL" : "PASS") + "\nUnity=" + Application.unityVersion + "\n" + string.Join("\n", Results));
        if (failed) throw new InvalidOperationException("Network edge validation failed; see result file");
    }

    private static T Add<T>(GameObject owner) where T : TSMPNetworkBehaviour
    {
#if UDONSHARP
        return owner.AddUdonSharpComponent<T>();
#else
        return owner.AddComponent<T>();
#endif
    }

    private static void BlendshapeOverwrite()
    {
        var owner = new GameObject("Blendshape Receiver");
        var other = new GameObject("Replacement Renderer");
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        try
        {
            var delta = new[] { Vector3.up, Vector3.up, Vector3.up };
            mesh.AddBlendShapeFrame("Selected", 100, delta, new Vector3[3], new Vector3[3]);
            mesh.AddBlendShapeFrame("Excluded", 100, delta, new Vector3[3], new Vector3[3]);
            var renderer = owner.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            var sync = Add<TSMPNetworkBlendShapesSync>(owner);
            sync.targetRenderer = renderer;
            sync.blendShapeCount = 2;
            sync.blendShapeIndices = new[] { 0 };
            sync.blendShapeBytes = new byte[] { 1, 2, 0, 0, 50, 1, 0, 80 };
            sync.OnTSMPVariableReceived();
            Check(renderer.GetBlendShapeWeight(0) == 50, "First value was not applied");
            Check(renderer.GetBlendShapeWeight(1) == 0, "Unselected shape was applied");
            renderer.SetBlendShapeWeight(0, 0);
            sync.OnTSMPVariableReceived();
            Results.Add("Blendshape weight after external reset and repeated 50=" + renderer.GetBlendShapeWeight(0));
            Check(renderer.GetBlendShapeWeight(0) == 50, "Repeated value did not restore externally changed shape");
            sync.OnTSMPVariableReceived();
            Check(renderer.GetBlendShapeWeight(0) == 50, "Unchanged value differs");

            var replacement = other.AddComponent<SkinnedMeshRenderer>();
            replacement.sharedMesh = mesh;
            sync.targetRenderer = replacement;
            sync.OnTSMPVariableReceived();
            Check(replacement.GetBlendShapeWeight(0) == 50, "Same-size renderer replacement did not receive the value");

            replacement.SetBlendShapeWeight(0, 0);
            sync.receiveInterpolation = ReceiveInterpolationMode.None;
            sync.OnTSMPVariableReceived();
            Check(replacement.GetBlendShapeWeight(0) == 0, "None changed renderer");
            sync.receiveInterpolation = ReceiveInterpolationMode.Discrete;
            sync.enabled = false;
            sync.OnTSMPVariableReceived();
            Check(replacement.GetBlendShapeWeight(0) == 0, "Disabled component changed renderer");
            sync.enabled = true;
            sync.OnTSMPVariableReceived();
            Check(replacement.GetBlendShapeWeight(0) == 50, "Re-enabled component did not restore shape");

            sync.blendShapeBytes = new byte[] { 1, 2, 0, 0, 100 };
            sync.OnTSMPVariableReceived();
            Check(replacement.GetBlendShapeWeight(0) == 50, "Truncated packet changed renderer");

            sync.receiveInterpolation = ReceiveInterpolationMode.Continuous;
            sync.continuousInterpolationRate = 0;
            sync.blendShapeBytes = new byte[] { 1, 1, 0, 0, 75 };
            sync.OnTSMPVariableReceived();
            var tick = typeof(TSMPNetworkBlendShapesSync).GetMethod("ApplyContinuousBlendShapes", BindingFlags.Instance | BindingFlags.NonPublic);
            tick.Invoke(sync, null);
            tick.Invoke(sync, null);
            Check(replacement.GetBlendShapeWeight(0) == 75, "Continuous target did not settle");
            replacement.SetBlendShapeWeight(0, 0);
            sync.receiveInterpolation = ReceiveInterpolationMode.Discrete;
            sync.OnTSMPVariableReceived();
            Check(replacement.GetBlendShapeWeight(0) == 75, "Repeated value after changing to Discrete was lost");
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(other);
            Object.DestroyImmediate(mesh);
        }
    }

    private static void AnimatorPacket(int parameters, int layers, bool mixed)
    {
        var owner = new GameObject("Animator Sender");
        var receiverOwner = new GameObject("Animator Receiver");
        var controller = new AnimatorController();
        controller.AddLayer("Base Layer");
        controller.layers[0].stateMachine.AddState("Idle");
        controller.AddParameter("Flag", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Count", AnimatorControllerParameterType.Int);
        controller.AddParameter("Amount", AnimatorControllerParameterType.Float);
        try
        {
            var animator = owner.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0);
            animator.SetBool("Flag", true);
            animator.SetInteger("Count", 42);
            animator.SetFloat("Amount", 3.5f);
            var sync = Add<TSMPNetworkAnimatorSync>(owner);
            sync.animator = animator;
            var names = new List<string>();
            var types = new List<byte>();
            var selectedLayers = new List<int>();
            for (int i = 0; i < parameters; i++)
            {
                if (mixed)
                {
                    names.Add(null); types.Add(3);
                    names.Add("Flag"); types.Add(255);
                }
                int type = mixed ? i % 3 + 1 : 3;
                names.Add(type == 1 ? "Amount" : type == 2 ? "Count" : "Flag");
                types.Add((byte)type);
            }
            for (int i = 0; i < layers; i++)
            {
                if (mixed) selectedLayers.Add(-1);
                selectedLayers.Add(0);
                if (mixed) selectedLayers.Add(256);
            }
            sync.parameterNames = names.ToArray();
            sync.parameterTypes = types.ToArray();
            sync.layerIndices = selectedLayers.ToArray();
            sync.TSMPBeforeEncode();
            int expectedParameters = Math.Min(255, parameters);
            int expectedLayers = Math.Min(255, layers);
            ValidatePacket(sync, expectedParameters, expectedLayers);
            byte[] previous = sync.animatorBytes;
            sync.TSMPBeforeEncode();
            ValidatePacket(sync, expectedParameters, expectedLayers);
            Check(ReferenceEquals(previous, sync.animatorBytes), "Same-size packet was reallocated");

            var target = receiverOwner.AddComponent<Animator>();
            target.runtimeAnimatorController = controller;
            target.Rebind();
            target.Update(0);
            var receiver = Add<TSMPNetworkAnimatorSync>(receiverOwner);
            receiver.animator = target;
            receiver.parameterNames = new[] { "Flag", "Count", "Amount" };
            receiver.parameterTypes = new byte[] { 3, 2, 1 };
            receiver.animatorBytes = sync.animatorBytes;
            receiver.OnTSMPVariableReceived();
            if (parameters > 0) Check(target.GetBool("Flag"), "Bool did not round-trip");
            if (mixed)
            {
                Check(target.GetInteger("Count") == 42, "Int did not round-trip");
                Check(target.GetFloat("Amount") == 3.5f, "Float did not round-trip");
            }

            sync.parameterNames = new[] { "Flag", "Count" };
            sync.parameterTypes = new byte[] { 3 };
            sync.layerIndices = new[] { -1, 1, 256 };
            sync.TSMPBeforeEncode();
            ValidatePacket(sync, 1, 0);
            sync.parameterTypes = null;
            sync.layerIndices = null;
            sync.TSMPBeforeEncode();
            ValidatePacket(sync, 0, 0);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(receiverOwner);
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                foreach (ChildAnimatorState state in layer.stateMachine.states) Object.DestroyImmediate(state.state);
                Object.DestroyImmediate(layer.stateMachine);
            }
            Object.DestroyImmediate(controller);
        }
    }

    private static void InspectorLimits()
    {
        var owner = new GameObject("Animator Inspector Selection");
        try
        {
            var sync = Add<TSMPNetworkAnimatorSync>(owner);
            var editor = typeof(K13A.TSMP.Editor.TSMPNetworkAnimatorSyncEditor);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var allParameters = editor.GetMethod("SetAllParameters", flags);
            var parameter = editor.GetMethod("SetParameter", flags);
            var layer = editor.GetMethod("SetLayer", flags);
            var parameters = new AnimatorControllerParameter[300];
            for (int i = 0; i < parameters.Length; i++)
                parameters[i] = new AnimatorControllerParameter { name = "Parameter" + i, type = AnimatorControllerParameterType.Bool };
            allParameters.Invoke(null, new object[] { sync, parameters, true });
            Check(sync.parameterNames.Length == 255 && sync.parameterTypes.Length == 255, "All Parameters exceeded packet limit");
            parameter.Invoke(null, new object[] { sync, "Parameter255", (byte)3, true });
            Check(sync.parameterNames.Length == 255, "Individual parameter selection exceeded limit");
            parameter.Invoke(null, new object[] { sync, "Parameter0", (byte)3, false });
            parameter.Invoke(null, new object[] { sync, "Parameter255", (byte)3, true });
            parameter.Invoke(null, new object[] { sync, "Parameter255", (byte)3, true });
            Check(sync.parameterNames.Length == 255 && sync.parameterNames[254] == "Parameter255", "Parameter deselect/reselect or duplicate check failed");

            sync.layerIndices = new int[255];
            for (int i = 0; i < 255; i++) sync.layerIndices[i] = i;
            layer.Invoke(null, new object[] { sync, 255, true });
            Check(sync.layerIndices.Length == 255, "Individual layer selection exceeded limit");
            layer.Invoke(null, new object[] { sync, 0, false });
            layer.Invoke(null, new object[] { sync, 256, true });
            Check(sync.layerIndices.Length == 254, "Unrepresentable layer index was selected");
            layer.Invoke(null, new object[] { sync, 255, true });
            layer.Invoke(null, new object[] { sync, 255, true });
            Check(sync.layerIndices.Length == 255 && sync.layerIndices[254] == 255, "Layer deselect/reselect or duplicate check failed");
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    private static void ValidatePacket(TSMPNetworkAnimatorSync sync, int parameters, int layers)
    {
        byte[] bytes = sync.animatorBytes;
        Check(bytes[0] == 1 && bytes[4] == 0, "Packet version/reserved byte changed");
        Check(bytes[2] == parameters && bytes[3] == layers, "Encoded counts differ from bounded valid selections");
        Check(sync.encodedParameterCount == parameters && sync.encodedLayerCount == layers, "Diagnostic counts differ");
        Check(bytes[1] == ((parameters > 0 ? 1 : 0) | (layers > 0 ? 2 : 0)), "Flags differ from counts");
        int cursor = 5;
        for (int i = 0; i < parameters; i++)
        {
            byte type = bytes[cursor + 4];
            Check(type >= 1 && type <= 3, "Invalid parameter type was packed");
            cursor += 5 + (type == 3 ? 1 : 4);
        }
        for (int i = 0; i < layers; i++)
        {
            Check(bytes[cursor] == 0, "Invalid layer was packed");
            cursor += 13;
        }
        Check(cursor == bytes.Length && cursor == sync.encodedAnimatorBytes, "Packet allocation, counts and write cursor differ");
    }

    private static void Test(string name, Action run)
    {
        try { run(); Results.Add("PASS " + name); }
        catch (Exception exception) { failed = true; Results.Add("FAIL " + name + "\n" + exception); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
