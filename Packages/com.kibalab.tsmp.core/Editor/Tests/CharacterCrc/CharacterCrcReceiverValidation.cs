#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using K13A.TSMP.Udon;
#if UDONSHARP
using UdonSharp;
using UdonSharpEditor;
using VRC.Udon;
#endif

namespace K13A.TSMP.Editor.Tests
{
    public static class CharacterCrcReceiverValidation
    {
        static void Check(bool value, string message) { CharacterCrcValidation.Require(value, message); }
        static T Add<T>(GameObject go) where T : TSMPBehaviour
        {
#if UDONSHARP
            return go.AddUdonSharpComponent<T>();
#else
            return go.AddComponent<T>();
#endif
        }
        public static void Recheck(byte[] bytes)
        {
            int m, chars, ranges; Check(CharacterCrcProtocol.TryLayout(bytes, bytes.Length, out m, out chars, out ranges), "Recheck directory");
            uint[] table = Crc32Runtime.EnsureTable(null);
            for (int i = 0; i < chars; i++)
            {
                int d = m + 16 + i * 16, p = Binary.ReadUInt16LE(bytes, d), n = Binary.ReadUInt16LE(bytes, d + 2);
                uint body = 0xffffffffu, finger = 0xffffffffu;
                Check(CharacterCrcProtocol.PoseCrc(bytes, p, n, false, table, ref body), "body input");
                Check(CharacterCrcProtocol.PoseCrc(bytes, p, n, true, table, ref finger), "finger input");
                int first = Binary.ReadUInt16LE(bytes, d + 4), count = bytes[d + 6];
                for (int j = first; j < first + count; j++)
                { int rp = m + 16 + chars * 16 + j * 4; body = CharacterCrcProtocol.Update(body, bytes, Binary.ReadUInt16LE(bytes, rp), Binary.ReadUInt16LE(bytes, rp + 2), table); }
                Binary.WriteUInt32LE(bytes, d + 8, body ^ 0xffffffffu); Binary.WriteUInt32LE(bytes, d + 12, finger ^ 0xffffffffu);
            }
            uint crc; Check(CharacterCrcProtocol.MetadataCrc(bytes, bytes.Length, table, out crc), "metadata input"); Binary.WriteUInt32LE(bytes, m + 12, crc);
        }
        static int Bone(byte[] bytes, int pose, int id)
        { int n = Binary.ReadUInt16LE(bytes, pose + 2); for (int i = 0; i < n; i++) if (bytes[pose + 4 + i * 6] == id) return pose + 5 + i * 6; throw new Exception("Bone missing"); }

        public static IEnumerator Run(string fixtures, string reports)
        {
            var root = new GameObject("Character CRC receiver integration");
            var decoder = Add<TSMPDecoder>(root); decoder.applyEveryFrame = false; decoder.debugLog = false;
            var image = Luma4Raster.CreateTexture(1920, 1080); Color32[] pixels = null;
            var byteTexture = new RenderTexture(256, 32, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); byteTexture.Create();
            Material codecMaterial = null;
            var log = new List<string>(); uint frame = 1;
            try
            {
                Type codecType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("K13A.TSMP.TSMPCodecLuma4")).First(t => t != null);
#if UDONSHARP
                var codec = (TSMPCodec)root.AddUdonSharpComponent(codecType);
#else
                var codec = (TSMPCodec)root.AddComponent(codecType);
#endif
                codecMaterial = new Material(AssetDatabase.LoadAssetAtPath<Material>("Packages/com.kibalab.tsmp.codec.luma4/Runtime/Materials/GPUDecoderLuma4.mat"));
                codecType.GetField("byteDecodeMaterial").SetValue(codec, codecMaterial);
                codec.codecId = 0;
                decoder.codecHandlers = new[] { codec }; decoder.sourceTexture = image; decoder.payloadByteTexture = byteTexture; decoder.blockSize = 16; decoder.sampleSize = 1; decoder.flipY = true;
                decoder.characterCrcMaterial = Resources.Load<Material>("TSMPCharacterCrc"); decoder.readbackPackMaterial = Resources.Load<Material>("TSMPReadbackPack");
                byte[] good = File.ReadAllBytes(Path.Combine(fixtures, "sender-1-0.bin"));
                int m, chars, ranges; Check(CharacterCrcProtocol.TryLayout(good, good.Length, out m, out chars, out ranges), "fixture");
                var targets = new List<Component>(); var ids = new List<ushort>(); var hashes = new List<uint>(); var fields = new List<string>();
                var poses = new List<TSMPNetworkHumanoidPoseSync>(); var placements = new List<TSMPNetworkTransformSync>();
                var boneTargets = new List<Transform[]>();
                int messages, cursor; Check(NetworkFrameReader.TryReadNetworkFrameHeader(good, out messages, out cursor), "fixture messages");
                for (int i = 0; i < messages; i++)
                {
                    ushort id; int mt, body, end, next; Check(NetworkFrameReader.TryReadMessageHeader(good, cursor, m, out id, out mt, out body, out end, out next), "message");
                    uint hash; int vt, value, length, after; Check(NetworkFrameReader.TryReadVariableEntry(good, body + 2, end, out hash, out vt, out value, out length, out after), "value");
                    var go = new GameObject("Target " + id); go.transform.SetParent(root.transform); Component target;
                    if (id == 201 || id == 202)
                    {
                        var pose = Add<TSMPNetworkHumanoidPoseSync>(go); pose.autoResolveBones = false; pose.receiveInterpolation = ReceiveInterpolationMode.Discrete; pose.includeFingerBones = true;
                        int count = Binary.ReadUInt16LE(good, value + 2); pose.boneIds = Enumerable.Range(0, count).Select(b => (int)good[value + 4 + b * 6]).ToArray();
                        CharacterCrcValidation.Invoke(pose, "EnsureLookupArrays");
                        var bones = (Transform[])CharacterCrcValidation.Get(pose, "_boneTargetsById");
                        foreach (int bid in pose.boneIds) { bones[bid] = new GameObject("Bone " + bid).transform; bones[bid].SetParent(go.transform); }
                        foreach (int bid in pose.boneIds) if (bid >= 24 && bid <= 53) bones[bid].SetParent(bones[bid <= 38 ? 17 : 18]);
                        CharacterCrcValidation.Set(pose, "_rootMotionTarget", bones[0]);
                        target = pose; poses.Add(pose); boneTargets.Add(bones); fields.Add("poseBytes");
                    }
                    else
                    {
                        var transform = Add<TSMPNetworkTransformSync>(go); transform.target = go.transform; transform.receiveInterpolation = ReceiveInterpolationMode.Discrete;
                        target = transform; placements.Add(transform); fields.Add("packedBytes");
                    }
                    targets.Add(target); ids.Add(id); hashes.Add(hash); cursor = next;
                }
                decoder.bindingTargets = targets.ToArray(); decoder.bindingNetworkIds = ids.ToArray(); decoder.bindingVariableHashes = hashes.ToArray(); decoder.bindingValueTypes = Enumerable.Repeat((byte)NetworkFrameProtocol.ValueTypeRawBytes, ids.Count).ToArray(); decoder.bindingFieldNames = fields.ToArray(); decoder.bindingDirections = new int[ids.Count];
#if UDONSHARP
                decoder.bindingUdonTargets = targets.Cast<UdonSharpBehaviour>().Select(p => UdonSharpEditorUtility.GetBackingUdonBehaviour(p)).ToArray();
                foreach (var p in targets.Cast<UdonSharpBehaviour>()) UdonSharpEditorUtility.CopyProxyToUdon(p);
#endif
                // Real raster encoding -> snapshot -> Luma4 GPU decode -> CRC -> async readback -> dispatch.
                Write(image, ref pixels, good, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.lastFrameValid && decoder.lastAppliedVariableCount == 7 && decoder.crcBodyRejected == 0, "Initial full receive: " + decoder.lastError + " valid=" + decoder.lastFrameValid + " header=" + decoder.lastHeaderValid + " vars=" + decoder.lastAppliedVariableCount + " meta=" + decoder.crcMetadataRejected + " bodyBad=" + decoder.crcBodyRejected + " frame=" + decoder.lastFrameIndex + " checked=" + decoder.crcFramesChecked);
                log.Add("PASS full Luma4 / CRC / async readback / 7 variable dispatches");
                int p0 = Binary.ReadUInt16LE(good, m + 16), arm = Bone(good, p0, 7), finger = Bone(good, p0, 24);
                Quaternion oldFinger = boneTargets[0][24].localRotation;
                byte[] newer = (byte[])good.Clone(); Binary.WritePackedQuaternion12LE(newer, arm, Quaternion.Euler(55, 10, 5)); Binary.WritePackedQuaternion12LE(newer, finger, Quaternion.Euler(70, 0, 0)); Recheck(newer);
                byte[] badFinger = (byte[])newer.Clone(); badFinger[finger] ^= 1;
                Write(image, ref pixels, badFinger, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.lastFrameValid && decoder.crcFingerRejected == 1 && decoder.crcBodyRejected == 0, "Finger rejection counters");
                Check(Quaternion.Angle(boneTargets[0][7].localRotation, Binary.ReadPackedQuaternion12LE(newer, arm)) < 0.1f, "Body must advance on finger failure");
                Check(Quaternion.Angle(boneTargets[0][24].localRotation, oldFinger) < 0.01f, "Finger local pose must hold");
                log.Add("PASS finger-only failure: Body advances, finger local pose held");
                Write(image, ref pixels, newer, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(Quaternion.Angle(boneTargets[0][24].localRotation, Binary.ReadPackedQuaternion12LE(newer, finger)) < 0.1f, "Finger recovery");
                byte[] badBody = (byte[])good.Clone(); badBody[arm] ^= 8;
                Vector3 sentinel = new Vector3(99, 98, 97); placements[0].target.localPosition = sentinel;
                Quaternion bodyBefore = boneTargets[0][7].localRotation, fingerBefore = boneTargets[0][24].localRotation;
                Write(image, ref pixels, badBody, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.lastAppliedVariableCount == 4 && decoder.crcBodyRejected == 1, "All 3 protected members must be skipped");
                Check(placements[0].target.localPosition == sentinel && Quaternion.Angle(bodyBefore, boneTargets[0][7].localRotation) < 0.01f && Quaternion.Angle(fingerBefore, boneTargets[0][24].localRotation) < 0.01f, "Atomic Body/Transform/finger hold");
                log.Add("PASS Body failure holds Pose and both Transforms; other character continues");
                byte[] badMeta = (byte[])good.Clone(); badMeta[8] ^= 1;
                int checkedBefore = decoder.crcBodyChecked;
                Write(image, ref pixels, badMeta, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(!decoder.lastFrameValid && decoder.crcMetadataRejected == 1 && decoder.crcBodyChecked == checkedBefore && placements[0].target.localPosition == sentinel, "Metadata failure must apply nothing");
                log.Add("PASS routing metadata failure rejects before dispatch");
                int rejectedChecked = decoder.crcFramesChecked;
                decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.crcFramesChecked == rejectedChecked && decoder.crcMetadataRejected == 1 && decoder.lastAppliedVariableCount == 0, "Rejected frame must not inflate CRC counts on repeat");
                Write(image, ref pixels, good, frame++, CharacterCrcProtocol.PayloadType, true); decoder.DecodeNow(); yield return Drain(decoder);
                Check(!decoder.lastFrameValid && decoder.crcMetadataRejected == 2 && decoder.lastAppliedVariableCount == 0, "Header/inner sequence mismatch must reject atomically");
                log.Add("PASS repeated bad frame counted once; header/inner sequence mismatch rejected");
                foreach (var pose in poses) { pose.receiveInterpolation = ReceiveInterpolationMode.Continuous; pose.ResetReceiveState(); }
                Write(image, ref pixels, good, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                var fingerTargets = (Quaternion[])CharacterCrcValidation.Get(poses[0], "_targetBoneRotations");
                // First receive after Reset snaps; next valid receive establishes interpolation targets.
                Write(image, ref pixels, good, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Quaternion continuousFinger = fingerTargets[24];
                Write(image, ref pixels, badFinger, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(Quaternion.Angle(((Quaternion[])CharacterCrcValidation.Get(poses[0], "_targetBoneRotations"))[24], continuousFinger) < 0.01f, "Continuous finger target must hold");
                log.Add("PASS reset and continuous interpolation target hold");
                byte[] legacy = File.ReadAllBytes(Path.Combine(fixtures, "legacy.bin"));
                Write(image, ref pixels, legacy, frame++, NetworkFrameProtocol.PayloadTypeNetworkFrame); decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.lastFrameValid && !decoder.lastCharacterCrcActive && !poses[0].crcSkipFingerUpdate, "Legacy transition must clear finger suppression");
                Write(image, ref pixels, good, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.lastFrameValid && decoder.lastCharacterCrcActive, "CRC re-enable");
                int checkedAfter = decoder.crcFramesChecked; decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.crcFramesChecked == checkedAfter && decoder.skippedDuplicateFrameCount > 0, "Duplicate frame counted twice");
                // Two snapshots in flight with different CRC results; buffers/results must remain paired.
                Write(image, ref pixels, badFinger, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow();
                Write(image, ref pixels, good, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow();
                Check(decoder.pendingDecodeCount == 2, "Overlap did not use two slots"); yield return Drain(decoder);
                Check(decoder.lastFrameValid && decoder.lastFrameIndex == frame - 1 && !poses[0].crcSkipFingerUpdate, "Overlapping result association");
                log.Add("PASS CRC off/on, duplicate suppression, predicted readback, 2-slot frame/result association");
                // Animator-root-space rotations are the production character path.
                poses[0].animator = poses[0].gameObject.AddComponent<Animator>();
                poses[0].transform.rotation = Quaternion.Euler(0,35,0);
                poses[0].receiveInterpolation = ReceiveInterpolationMode.Continuous;
                poses[0].ResetReceiveState();
                byte[] rooted = (byte[])good.Clone(); rooted[p0+1] = (byte)((rooted[p0+1] & ~1) | 4); Recheck(rooted);
                Write(image, ref pixels, rooted, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Write(image, ref pixels, rooted, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Quaternion localFingerTarget = ((Quaternion[])CharacterCrcValidation.Get(poses[0], "_targetBoneRotations"))[24];
                Quaternion oldWristTarget = ((Quaternion[])CharacterCrcValidation.Get(poses[0], "_targetBoneRotations"))[17];
                byte[] rootedBad = (byte[])rooted.Clone();
                Binary.WritePackedQuaternion12LE(rootedBad, Bone(rootedBad,p0,17), Quaternion.Euler(80,10,0));
                Binary.WritePackedQuaternion12LE(rootedBad, finger, Quaternion.Euler(95,0,0)); Recheck(rootedBad); rootedBad[finger] ^= 1;
                Write(image, ref pixels, rootedBad, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.lastFrameValid && Quaternion.Angle(localFingerTarget, ((Quaternion[])CharacterCrcValidation.Get(poses[0], "_targetBoneRotations"))[24]) < 0.01f, "Animator-root-space finger target was converted against new wrist");
                Check(Quaternion.Angle(oldWristTarget, ((Quaternion[])CharacterCrcValidation.Get(poses[0], "_targetBoneRotations"))[17]) > 20f, "Root-space Body wrist did not advance");
                poses[0].alternateHandUpdates = true; poses[0].alternateIncludeWrists = true;
                Write(image, ref pixels, rooted, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Write(image, ref pixels, rooted, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                bool nextHand = (bool)CharacterCrcValidation.Get(poses[0], "_receiveRightHandNext");
                Write(image, ref pixels, rootedBad, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check((bool)CharacterCrcValidation.Get(poses[0], "_receiveRightHandNext") == nextHand, "Bad fingers advanced alternating-hand schedule");
                log.Add("PASS animator-root-space local finger target hold while wrist advances; alternating-hand schedule preserved");
                int fingersChecked = decoder.crcFingerChecked, bodiesBad = decoder.crcBodyRejected;
                byte[] excluded = File.ReadAllBytes(Path.Combine(fixtures,"sender-0-0.bin")); excluded[finger] ^= 1;
                Write(image, ref pixels, excluded, frame++, CharacterCrcProtocol.PayloadType); decoder.DecodeNow(); yield return Drain(decoder);
                Check(decoder.lastFrameValid && decoder.crcFingerChecked == fingersChecked && decoder.crcBodyRejected == bodiesBad && !poses[0].crcSkipFingerUpdate, "Excluded fingers must remain unprotected and applied");
                log.Add("PASS excluded finger corruption does not reject Body or set finger hold");
                Check(decoder.predictedReadbackCount > 0 && decoder.predictionFallbackCount > 0, "Prediction/fallback paths not exercised");
                log.Add("predicted=" + decoder.predictedReadbackCount + " fallback=" + decoder.predictionFallbackCount + " combined=" + decoder.combinedByteOutputCount);
                File.WriteAllLines(Path.Combine(reports, "receiver-integration.txt"), log);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(image); byteTexture.Release(); UnityEngine.Object.DestroyImmediate(byteTexture); if (codecMaterial != null) UnityEngine.Object.DestroyImmediate(codecMaterial);
            }
        }
        static void Write(Texture2D image, ref Color32[] pixels, byte[] packet, uint index, int type, bool sequenceMismatch = false)
        {
            // Change the inner sequence without repairing intentional metadata corruption.
            packet = (byte[])packet.Clone();
            uint[] table = Crc32Runtime.EnsureTable(null); uint oldActual = 0, newActual;
            int m = 0, chars, ranges;
            bool protectedPacket = type == CharacterCrcProtocol.PayloadType;
            if (protectedPacket) { Check(CharacterCrcProtocol.TryLayout(packet, packet.Length, out m, out chars, out ranges), "test layout"); Check(CharacterCrcProtocol.MetadataCrc(packet, packet.Length, table, out oldActual), "test metadata"); }
            Binary.WriteUInt32LE(packet, NetworkFrameProtocol.NetworkSequenceOffset, sequenceMismatch ? index + 1 : index);
            if (protectedPacket) { Check(CharacterCrcProtocol.MetadataCrc(packet, packet.Length, table, out newActual), "updated test metadata"); Binary.WriteUInt32LE(packet, m + 12, Binary.ReadUInt32LE(packet, m + 12) ^ oldActual ^ newActual); }
            FrameHeader header = FrameHeader.CreateDefault(); header.BlockSize = 16; header.ActiveWidthBlocks = 120; header.ActiveHeightBlocks = 67; header.StreamId = 123; header.FrameIndex = index; header.CodecId = 0; header.PayloadType = (PayloadType)type; header.PayloadSize = (ushort)packet.Length; header.DecodeSampleSize = 1;
            byte[] bytes = new byte[FrameHeader.Size]; header.WriteTo(bytes, 0); string error;
            Check(Luma4Raster.TryWriteFrameBuffered(image, 16, bytes, packet, ref pixels, out error), error);
        }
        static IEnumerator Drain(TSMPDecoder decoder)
        {
            double until = EditorApplication.timeSinceStartup + 15;
            do { yield return null; CharacterCrcValidation.Invoke(decoder, "ProcessReadyReadbacks"); } while (decoder.pendingDecodeCount > 0 && EditorApplication.timeSinceStartup < until);
            Check(decoder.pendingDecodeCount == 0, "GPU readback timed out");
        }
    }
}
#endif
