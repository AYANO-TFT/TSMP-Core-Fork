// Copy into an Editor folder to run through an existing Unity Editor.
// C# regression checks; actual Udon VM checks are separate.
using System;
using System.Collections.Generic;
using System.Reflection;
using K13A.TSMP;
using K13A.TSMP.Udon;
using UnityEngine;

public static class HumanoidReceiveValidation
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Quaternion A = Quaternion.Euler(23, -36, 11);
    static readonly Quaternion B = Quaternion.Euler(-19, 52, 31);
    static readonly Vector3 P = new Vector3(.3f, 1.2f, -.4f);
    public static object Field(object p, string n) => p.GetType().GetField(n, Private).GetValue(p);
    public static void Set(object p, string n, object v) => p.GetType().GetField(n, Private).SetValue(p, v);
    public static object Call(object p, string n, params object[] args) => p.GetType().GetMethod(n, Private).Invoke(p, args);
    static void Require(bool b, string m) { if (!b) throw new Exception(m); }
    static void Rotation(Quaternion a, Quaternion b, string m) { Require(Quaternion.Angle(a, b) < .05f, m + ": " + Quaternion.Angle(a, b)); }
    static void Position(Vector3 a, Vector3 b, string m) { Require(Vector3.Distance(a, b) < .0001f, m); }
    public static byte[] Packet(byte version, byte flags, int[] ids, bool root = false)
    {
        int tail = root ? 12 + (version >= 4 ? 5 : 0) : 0;
        var b = new byte[4 + ids.Length * 6 + tail]; b[0] = version; b[1] = flags;
        Binary.WriteUInt16LE(b, 2, (ushort)ids.Length);
        for (int i = 0; i < ids.Length; i++) { b[4 + i * 6] = (byte)ids[i]; Binary.WritePackedQuaternion12LE(b, 5 + i * 6, i == 0 ? A : B); }
        if (root) { Binary.WriteVector3Float32LE(b, 4 + ids.Length * 6, P); if (version >= 4) Binary.WritePackedQuaternion12LE(b, 16 + ids.Length * 6, B); }
        return b;
    }
    sealed class Rig : IDisposable
    {
        public GameObject root;
        public TSMPNetworkHumanoidPoseSync p;
        public Transform hips, spine, bridge, chest;
        public Rig(Func<GameObject, TSMPNetworkHumanoidPoseSync> make, bool intermediate = false)
        {
            root = new GameObject("Humanoid receive regression") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetPositionAndRotation(new Vector3(3, 2, 1), Quaternion.Euler(13, 41, -9));
            p = make(root); p.animator = root.AddComponent<Animator>(); p.autoResolveBones = false; p.includeFingerBones = false;
            p.boneIds = new[] { 0, 7, 8, 54 }; p.receiveInterpolation = ReceiveInterpolationMode.Discrete; p.continuousInterpolationRate = 0;
            Call(p, "EnsureFingerBoneIds"); Call(p, "RebuildBoneLookup");
            hips = Child(root.transform, "Hips"); spine = Child(hips, "Spine");
            bridge = intermediate ? Child(spine, "Unmapped bridge") : spine;
            chest = Child(bridge, "Chest");
            var targets = (Transform[])Field(p, "_boneTargetsById"); targets[0] = hips; targets[7] = spine; targets[8] = chest;
            targets[54] = Child(chest, "UpperChest");
            Set(p, "_rootMotionTarget", hips); Call(p, "RebuildBoneParentLookup");
        }
        static Transform Child(Transform parent, string n)
        {
            var t = new GameObject(n).transform; t.SetParent(parent, false); t.localRotation = Quaternion.Euler(7, 11, 3); return t;
        }
        public void Apply(byte[] bytes, bool continuous)
        {
            p.receiveInterpolation = continuous ? ReceiveInterpolationMode.Continuous : ReceiveInterpolationMode.Discrete;
            p.poseBytes = bytes; p.OnTSMPVariableReceived();
        }
        public void Dispose() { UnityEngine.Object.DestroyImmediate(root); }
    }
    public static string[] Run(Func<GameObject, TSMPNetworkHumanoidPoseSync> make)
    {
        var passed = new List<string>();
        foreach (bool continuous in new[] { false, true })
        foreach (byte space in new byte[] { 0, 1, 4 })
        foreach (bool bridge in new[] { false, true })
        {
            using (var r = new Rig(make, bridge))
            {
                var bytes = Packet(4, space, new[] { 7, 8 });
                var a = Binary.ReadPackedQuaternion12LE(bytes, 5); var b = Binary.ReadPackedQuaternion12LE(bytes, 11);
                var wa = space == 4 ? r.root.transform.rotation * a : a;
                var wb = space == 4 ? r.root.transform.rotation * b : b;
                var parentA = r.spine.parent.rotation;
                var parentB = bridge ? wa * Quaternion.Inverse(r.spine.rotation) * r.bridge.rotation : wa;
                r.Apply(bytes, continuous);
                if (continuous)
                {
                    var targets = (Quaternion[])Field(r.p, "_targetBoneRotations");
                    Rotation(targets[7], space == 1 ? a : Quaternion.Inverse(parentA) * wa, "Spine target");
                    Rotation(targets[8], space == 1 ? b : Quaternion.Inverse(parentB) * wb, "Chest target");
                    Call(r.p, "ApplyContinuousPose");
                }
                Rotation(space == 1 ? r.spine.localRotation : r.spine.rotation, space == 1 ? a : wa, "Spine applied");
                Rotation(space == 1 ? r.chest.localRotation : r.chest.rotation, space == 1 ? b : wb, "Chest applied");
                passed.Add($"pose continuous={continuous}, space={space}, intermediate={bridge}");
            }
        }
        foreach (bool continuous in new[] { false, true })
        foreach (byte version in new byte[] { 2, 3, 4 })
        foreach (byte space in new byte[] { 0, 4, 8 })
        {
            using (var r = new Rig(make))
            {
                var bytes = Packet(version, (byte)(space | 2), new[] { 7, 8 }, true);
                r.Apply(bytes, continuous);
                if (continuous)
                {
                    // Separate TransformSync can move the Animator after receipt.
                    if (space == 4) r.root.transform.SetPositionAndRotation(new Vector3(-2, 1, 4), Quaternion.Euler(-12, 78, 18));
                    Call(r.p, "ApplyContinuousPose");
                }
                var expected = space == 4 ? r.root.transform.position + r.root.transform.rotation * P : P;
                Position(space == 0 ? r.hips.localPosition : r.hips.position, expected, "Root position");
                if (version == 4)
                {
                    var rotation = Binary.ReadPackedQuaternion12LE(bytes, 28);
                    Rotation(space == 0 ? r.hips.localRotation : r.hips.rotation, space == 4 ? r.root.transform.rotation * rotation : rotation, "Root rotation");
                }
                passed.Add($"root continuous={continuous}, version={version}, space={space}");
            }
        }
        foreach (bool continuous in new[] { false, true })
        foreach (string invalid in new[] { "duplicate", "unconfigured", "range", "short", "extra", "v0", "v5", "flags", "v1root", "shortroot", "extraroot" })
        {
            using (var r = new Rig(make))
            {
                byte[] b = Packet(4, 0, new[] { 7, 8 });
                if (invalid == "duplicate") b[10] = 7;
                if (invalid == "unconfigured") b[10] = (byte)HumanBodyBones.LeftEye;
                if (invalid == "range") b[10] = 255;
                if (invalid == "short") Array.Resize(ref b, b.Length - 1);
                if (invalid == "extra") Array.Resize(ref b, b.Length + 1);
                if (invalid == "v0") b[0] = 0;
                if (invalid == "v5") b[0] = 5;
                if (invalid == "flags") b[1] = 128;
                if (invalid == "v1root") { b[0] = 1; b[1] = 2; }
                if (invalid == "shortroot" || invalid == "extraroot")
                { b = Packet(4, 2, new[] { 7, 8 }, true); Array.Resize(ref b, b.Length + (invalid == "shortroot" ? -1 : 1)); }
                var a = r.spine.rotation; var c = r.chest.rotation; var h = r.hips.position;
                r.Apply(b, continuous);
                Rotation(r.spine.rotation, a, "Invalid frame must be atomic: spine " + invalid);
                Rotation(r.chest.rotation, c, "Invalid frame must be atomic: chest");
                Position(r.hips.position, h, "Invalid frame root");
                Require(!(bool)Field(r.p, "_hasContinuousBoneTargets"), "Invalid frame target flags");
                passed.Add($"reject {invalid}, continuous={continuous}");
            }
        }
        using (var r = new Rig(make))
        {
            ((Transform[])Field(r.p, "_boneTargetsById"))[7] = null;
            var bytes = Packet(4, 1, new[] { 7, 8 }); r.Apply(bytes, false);
            Rotation(r.chest.localRotation, Binary.ReadPackedQuaternion12LE(bytes, 11), "Missing target must not shift record cursor");
            passed.Add("missing bone target");
        }
        using (var r = new Rig(make))
        {
            r.p.ResetReceiveState(); var bytes = Packet(4, 1, new[] { 7, 8 }); r.Apply(bytes, true);
            Rotation(r.spine.localRotation, Binary.ReadPackedQuaternion12LE(bytes, 5), "First packet snaps after reset");
            Require(!(bool)Field(r.p, "_snapNextReceive"), "Snap state clears"); passed.Add("receive reset snap");
        }
        return passed.ToArray();
    }
}
