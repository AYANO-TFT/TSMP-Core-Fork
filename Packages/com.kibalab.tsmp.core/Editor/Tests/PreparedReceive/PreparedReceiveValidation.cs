using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using K13A.TSMP.Udon;
using UnityEngine;
#if UDONSHARP
using UdonSharpEditor;
#endif

namespace K13A.TSMP.Editor.Tests
{
    // Explicitly invoked validation; no production runtime behaviour or automatic menu.
    public static class PreparedReceiveValidation
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static object Get(object o, string name) => o.GetType().GetField(name, Fields).GetValue(o);
        private static void Set(object o, string name, object value) => o.GetType().GetField(name, Fields).SetValue(o, value);
        private static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Fields).Invoke(o, args);
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static T Add<T>(GameObject go) where T : TSMPBehaviour
        {
#if UDONSHARP
            return go.AddUdonSharpComponent<T>();
#else
            return go.AddComponent<T>();
#endif
        }

        public static string Run()
        {
            var results = new List<string>();
            foreach (CompressionMode packing in new[] { CompressionMode.Off, CompressionMode.RotOnly, CompressionMode.Full })
            foreach (bool local in new[] { false, true })
            foreach (bool rigidbody in new[] { false, true })
            foreach (ReceiveInterpolationMode interpolation in new[] { ReceiveInterpolationMode.Discrete, ReceiveInterpolationMode.Continuous })
            {
                CheckTransform(packing, local, rigidbody, interpolation);
                results.Add("PASS Transform " + packing + " local=" + local + " rigidbody=" + rigidbody + " interpolation=" + interpolation);
            }
            foreach (ReceiveInterpolationMode interpolation in new[] { ReceiveInterpolationMode.None, ReceiveInterpolationMode.Discrete, ReceiveInterpolationMode.Continuous })
            {
                CheckBlendShape(interpolation);
                results.Add("PASS BlendShape " + interpolation + " selection/invalid index/reset/disable/re-enable/invalidation");
            }
            CheckBindings();
            results.Add("PASS Decoder prepared lookup ordering/duplicate range/policies/malformed configuration");
            return string.Join("\n", results) + "\npassed=" + results.Count;
        }

        private static GameObject TransformRoot(string name)
        {
            var parent = new GameObject(name);
            parent.transform.SetPositionAndRotation(new Vector3(3, -2, 4), Quaternion.Euler(17, 43, -11));
            parent.transform.localScale = new Vector3(1.2f, .8f, 1.1f);
            return parent;
        }

        private static TSMPNetworkTransformSync TransformReceiver(GameObject parent, bool local, bool rigidbody, bool prepared, ReceiveInterpolationMode interpolation)
        {
            var go = new GameObject("Target"); go.transform.SetParent(parent.transform, false);
            var sync = Add<TSMPNetworkTransformSync>(go);
            sync.target = go.transform; sync.useLocalSpace = local; sync.syncRigidbody = rigidbody;
            if (rigidbody) { sync.targetRigidbody = go.AddComponent<Rigidbody>(); sync.targetRigidbody.isKinematic = true; }
            sync.receiveInterpolation = interpolation; sync.continuousInterpolationRate = 0;
            sync.cacheFixedReceiveConfiguration = prepared;
            if (prepared) Require(sync.EditorPrepareFixedReceiveConfiguration(), "Transform preparation failed");
            return sync;
        }

        private static void CheckTransform(CompressionMode packing, bool local, bool rigidbody, ReceiveInterpolationMode interpolation)
        {
            GameObject a = TransformRoot("Legacy Transform"), b = TransformRoot("Prepared Transform"), source = TransformRoot("Transform packet source");
            try
            {
                var legacy = TransformReceiver(a, local, rigidbody, false, interpolation);
                var prepared = TransformReceiver(b, local, rigidbody, true, interpolation);
                var sender = TransformReceiver(source, local, rigidbody, false, interpolation);
                sender.compressionMode = packing;
                sender.target.localPosition = new Vector3(1.5f, 2.25f, -3.75f);
                sender.target.localRotation = Quaternion.Euler(15, -30, 45);
                sender.target.localScale = new Vector3(1.5f, .75f, 2);
                sender.TSMPBeforeEncode();
                byte[] packet = (byte[])sender.packedBytes.Clone();
                for (int pass = 0; pass < 3; pass++)
                {
                    if (pass == 1) { legacy.ResetReceiveState(); prepared.ResetReceiveState(); }
                    if (pass == 2) { a.SetActive(false); b.SetActive(false); a.SetActive(true); b.SetActive(true); }
                    legacy.packedBytes = (byte[])packet.Clone(); prepared.packedBytes = (byte[])packet.Clone();
                    legacy.OnTSMPVariableReceived(); prepared.OnTSMPVariableReceived();
                    Call(legacy, "ApplyContinuousTarget"); Call(prepared, "ApplyContinuousTarget");
                    Require((legacy.target.position - prepared.target.position).sqrMagnitude < 1e-9f, "Transform position differs");
                    Require(Mathf.Abs(Quaternion.Dot(legacy.target.rotation, prepared.target.rotation)) > .999999f, "Transform rotation differs");
                    Require((legacy.target.localScale - prepared.target.localScale).sqrMagnitude < 1e-9f, "Transform scale differs");
                    Require((bool)Get(prepared, "_useFixedReceiveConfiguration"), "Prepared Transform did not execute");
                    if (rigidbody)
                        Require((legacy.targetRigidbody.position - prepared.targetRigidbody.position).sqrMagnitude < 1e-9f, "Rigidbody position differs");
                }
                Vector3 before = prepared.target.position;
                prepared.ResetReceiveState(); prepared.packedBytes = packet.Take(packet.Length - 1).ToArray(); prepared.OnTSMPVariableReceived();
                Require((prepared.target.position - before).sqrMagnitude < 1e-9f && (bool)Get(prepared, "_snapNextReceive"), "Malformed transform changed state");
                prepared._InvalidateReceiveConfiguration(); prepared.packedBytes = packet; prepared.OnTSMPVariableReceived();
                Require(!(bool)Get(prepared, "_useFixedReceiveConfiguration"), "Transform invalidation failed");
                prepared.cacheFixedReceiveConfiguration = false; prepared.EditorPrepareFixedReceiveConfiguration();
                Require((int)Get(prepared, "_preparedReceiveVersion") == 0, "Transform opt-out failed");
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(source); }
        }

        private static Mesh BlendMesh()
        {
            var mesh = new Mesh(); mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            for (int i = 0; i < 4; i++) mesh.AddBlendShapeFrame("Shape" + i, 100, new[] { Vector3.right, Vector3.up, Vector3.forward }, new Vector3[3], new Vector3[3]);
            return mesh;
        }

        private static TSMPNetworkBlendShapesSync BlendReceiver(GameObject go, Mesh mesh, bool prepared, ReceiveInterpolationMode interpolation)
        {
            var renderer = go.AddComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
            var sync = Add<TSMPNetworkBlendShapesSync>(go); sync.targetRenderer = renderer;
            sync.blendShapeIndices = new[] { 3, 1, 1, 0, -1, 99 }; sync.cacheFixedReceiveConfiguration = prepared;
            sync.receiveInterpolation = interpolation; sync.continuousInterpolationRate = 0;
            if (prepared) Require(sync.EditorPrepareFixedReceiveConfiguration(), "BlendShape preparation failed");
            for (int i = 0; i < 4; i++) renderer.SetBlendShapeWeight(i, 11 * (i + 1));
            return sync;
        }

        private static void CheckBlendShape(ReceiveInterpolationMode interpolation)
        {
            var a = new GameObject("Legacy BlendShape"); var b = new GameObject("Prepared BlendShape"); var mesh = BlendMesh();
            try
            {
                var legacy = BlendReceiver(a, mesh, false, interpolation); var prepared = BlendReceiver(b, mesh, true, interpolation);
                Require(((int[])Get(prepared, "_preparedInterpolationIndices")).SequenceEqual(new[] { 0, 1, 3 }), "Prepared interpolation order differs from legacy mesh order");
                byte[] packet = { 1, 5, 0, 0, 0, 1, 0, 50, 2, 0, 100, 3, 0, 255, 99, 0, 200 };
                object table = Get(prepared, "_selectedBlendShapeLookup");
                for (int pass = 0; pass < 3; pass++)
                {
                    if (pass == 1) { legacy.ResetReceiveState(); prepared.ResetReceiveState(); }
                    if (pass == 2) { Call(legacy, "OnDisable"); Call(prepared, "OnDisable"); a.SetActive(false); b.SetActive(false); a.SetActive(true); b.SetActive(true); }
                    legacy.blendShapeBytes = packet; prepared.blendShapeBytes = packet;
                    legacy.OnTSMPVariableReceived(); prepared.OnTSMPVariableReceived();
                    Call(legacy, "ApplyContinuousBlendShapes"); Call(prepared, "ApplyContinuousBlendShapes");
                    for (int i = 0; i < 4; i++) Require(legacy.targetRenderer.GetBlendShapeWeight(i) == prepared.targetRenderer.GetBlendShapeWeight(i), "BlendShape weight differs at " + i);
                    Require(prepared.targetRenderer.GetBlendShapeWeight(2) == 33, "Unselected BlendShape changed");
                    Require(ReferenceEquals(table, Get(prepared, "_selectedBlendShapeLookup")), "Reset/disable rebuilt fixed selection");
                }
                prepared._InvalidateBlendShapeCache();
                Require(!(bool)Get(prepared, "_useFixedReceiveConfiguration"), "BlendShape invalidation failed");
                prepared.cacheFixedReceiveConfiguration = false; prepared.EditorPrepareFixedReceiveConfiguration();
                Require((int)Get(prepared, "_preparedReceiveVersion") == 0, "BlendShape opt-out failed");
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); UnityEngine.Object.DestroyImmediate(mesh); }
        }

        private static void CheckBindings()
        {
            var root = new GameObject("Prepared binding validation");
            try
            {
                var first = TransformReceiver(root, true, false, true, ReceiveInterpolationMode.Continuous);
                var second = TransformReceiver(root, true, false, true, ReceiveInterpolationMode.None);
                var decoder = Add<TSMPDecoder>(root); decoder.cacheFixedBindings = true;
                decoder.bindingTargets = new Component[] { first, second, first };
#if UDONSHARP
                decoder.bindingUdonTargets = new[] { UdonSharpEditorUtility.GetBackingUdonBehaviour(first), UdonSharpEditorUtility.GetBackingUdonBehaviour(second), UdonSharpEditorUtility.GetBackingUdonBehaviour(first) };
#endif
                decoder.bindingNetworkIds = new ushort[] { 12, 3, 12 }; decoder.bindingVariableHashes = new uint[] { 9, 2, 9 };
                decoder.bindingFieldNames = new[] { "packedBytes", "packedBytes", "packedBytes" };
                decoder.bindingValueTypes = new byte[] { (byte)NetworkFrameProtocol.ValueTypeRawBytes, (byte)NetworkFrameProtocol.ValueTypeRawBytes, (byte)NetworkFrameProtocol.ValueTypeRawBytes };
                Require(decoder.EditorPrepareFixedBindings(), "Binding preparation failed");
                var expectedIds = new ushort[3]; var expectedHashes = new uint[3]; var expectedIndices = new int[3];
                BindingLookup.Build(expectedIds, expectedHashes, expectedIndices, 3, decoder.bindingNetworkIds, decoder.bindingVariableHashes);
                Require(((int[])Get(decoder, "_bindingLookupBindingIndices")).SequenceEqual(expectedIndices), "Prepared lookup changed tie ordering");
                Require(((int[])Get(decoder, "_preparedLookupEnds")).SequenceEqual(new[] { 1, 3, 3 }), "Duplicate binding ranges incorrect");
                Require(((byte[])Get(decoder, "_preparedReceivePolicies")).SequenceEqual(new byte[] { 1, 2, 1 }), "Fixed receive policies incorrect");
                first.cacheFixedReceiveConfiguration = false; decoder.EditorPrepareFixedBindings();
                Require(((byte[])Get(decoder, "_preparedReceivePolicies")).SequenceEqual(new byte[] { 0, 2, 0 }), "Mixed receiver opt-out not respected");
                decoder.bindingFieldNames = new string[1];
                Require(!decoder.EditorPrepareFixedBindings(), "Malformed binding lengths were accepted");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
