using System;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Endpoint = ReceivePolicyValidation.Endpoint;
using Object = UnityEngine.Object;

public static class AnimatorPolicyCases
{
    public static readonly string[] SelectionCases = { "selected", "excluded", "empty", "null", "changed", "invalid" };
    public static readonly string[] TimeCases = { "loop boundary", "loop correction", "loop cycles", "once cycles", "once completed", "once threshold", "state change", "crossfade change" };

    public static void Time(string test)
    {
        using (var fixture = new Fixture())
        using (var sync = new Endpoint(typeof(TSMPNetworkAnimatorSync)))
        {
            bool once = test.StartsWith("once");
            string state = once ? "Upper.Once" : "Upper.Loop";
            float before = test == "once completed" ? 1.25f : test.StartsWith("loop") ? 0.99f : 0.25f;
            float received = test == "loop boundary" ? 0.01f : test == "loop correction" ? 0.1f :
                test == "loop cycles" ? 2.99f : test == "once threshold" ? 0.26f : before + 1f;
            bool changeState = test.EndsWith("change");
            fixture.Animator.Play(state, 1, before);
            fixture.Animator.Update(0);
            Check(fixture.Animator.GetCurrentAnimatorStateInfo(1).loop == !once, "Fixture loop flag incorrect");
            sync.Set("animator", fixture.Animator);
            sync.Set("layerIndices", new[] { 1 });
            sync.Set("receiveInterpolation", ReceiveInterpolationMode.Discrete);
            sync.Set("normalizedTimeApplyThreshold", 0.03f);
            sync.Set("layerFadeDuration", test == "crossfade change" ? 0.1f : 0f);
            sync.Set("animatorBytes", Packet(1, Animator.StringToHash(changeState ? "Upper.Other" : state), received, 0.3f));
            sync.Call("OnTSMPVariableReceived");
            fixture.Animator.Update(0);
            if (test == "crossfade change")
                Check(fixture.Animator.IsInTransition(1) && fixture.Animator.GetNextAnimatorStateInfo(1).fullPathHash == Animator.StringToHash("Upper.Other"), "State change did not crossfade");
            else
            {
                var actual = fixture.Animator.GetCurrentAnimatorStateInfo(1);
                bool keep = test == "loop boundary" || test == "loop cycles" || test == "once threshold";
                Near(actual.normalizedTime, keep ? before : received, "Normalized time");
                Check(actual.fullPathHash == Animator.StringToHash(changeState ? "Upper.Other" : state), "State change was skipped");
            }
        }
    }

    public static void Selection(string selection)
    {
        using (var fixture = new Fixture())
        using (var sync = new Endpoint(typeof(TSMPNetworkAnimatorSync)))
        {
            sync.Set("animator", fixture.Animator);
            sync.Set("receiveInterpolation", ReceiveInterpolationMode.Discrete);
            int[] layers = selection == "null" ? null : selection == "empty" ? new int[0] : new[] { selection == "excluded" ? 0 : 1 };
            sync.Set("layerIndices", layers);
            if (selection == "changed") layers[0] = 0;
            int layer = selection == "invalid" ? 200 : 1;
            sync.Set("animatorBytes", Packet(layer, Animator.StringToHash("Upper.Other"), 0.6f, 0.8f));
            sync.Call("OnTSMPVariableReceived");
            fixture.Animator.Update(0);
            bool apply = selection == "selected";
            Near(fixture.Animator.GetLayerWeight(1), apply ? 0.8f : 0.3f, "Layer weight");
            var state = fixture.Animator.GetCurrentAnimatorStateInfo(1);
            Check(state.fullPathHash == Animator.StringToHash(apply ? "Upper.Other" : "Upper.Loop"), "Layer state changed unexpectedly");
            Near(state.normalizedTime, apply ? 0.6f : 0.25f, "Layer time");
        }
    }

    private static byte[] Packet(int layer, int hash, float time, float weight)
    {
        var bytes = new byte[18];
        bytes[0] = 1;
        bytes[1] = 2;
        bytes[3] = 1;
        bytes[5] = (byte)layer;
        Binary.WriteInt32LE(bytes, 6, hash);
        Binary.WriteFloat32LE(bytes, 10, time);
        Binary.WriteFloat32LE(bytes, 14, weight);
        return bytes;
    }

    private sealed class Fixture : IDisposable
    {
        private const string Path = "Assets/Validation/ReceivePolicies/AnimatorPolicy.controller";
        private readonly GameObject owner;
        public readonly Animator Animator;

        public Fixture()
        {
            AssetDatabase.DeleteAsset(Path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(Path);
            controller.AddLayer("Upper");
            foreach (var layer in controller.layers)
            {
                foreach (string name in new[] { "Loop", "Once", "Other" })
                {
                    var clip = new AnimationClip { name = name };
                    clip.SetCurve("", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0, 0, 1, 1));
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = name != "Once";
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AssetDatabase.AddObjectToAsset(clip, controller);
                    var state = layer.stateMachine.AddState(name);
                    state.motion = clip;
                }
            }
            AssetDatabase.SaveAssets();
            owner = new GameObject("Animator policy");
            Animator = owner.AddComponent<Animator>();
            Animator.runtimeAnimatorController = controller;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Animator.Rebind();
            Animator.Play("Upper.Loop", 1, 0.25f);
            Animator.SetLayerWeight(1, 0.3f);
            Animator.Update(0);
            Animator.speed = 0;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(owner);
            AssetDatabase.DeleteAsset(Path);
        }
    }

    private static void Near(float actual, float expected, string name)
    {
        Check(Mathf.Abs(actual - expected) < 0.001f, name + ": expected " + expected + ", got " + actual);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
