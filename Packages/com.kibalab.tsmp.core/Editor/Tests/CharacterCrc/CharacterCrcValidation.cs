#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using K13A.TSMP.Udon;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace K13A.TSMP.Editor.Tests
{
    public static class CharacterCrcValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        public static void Set(object target, string name, object value) { target.GetType().GetField(name, Private | BindingFlags.Public).SetValue(target, value); }
        public static object Get(object target, string name) { return target.GetType().GetField(name, Private | BindingFlags.Public).GetValue(target); }
        public static object Invoke(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Private | BindingFlags.Public).Invoke(target, args); }
        static uint Reference(byte[] bytes)
        {
            uint c = 0xffffffffu;
            foreach (byte b in bytes) { c ^= b; for (int i = 0; i < 8; i++) c = (c >> 1) ^ ((c & 1u) != 0 ? 0xedb88320u : 0u); }
            return c ^ 0xffffffffu;
        }

#if !UDONSHARP
        public static void ExportSenderFixtures(string folder)
        {
            Directory.CreateDirectory(folder);
            var root = new GameObject("CRC validation input");
            try
            {
                var sources = new List<TSMPNetworkBehaviour>(); var groups = new TSMPCharacterCrcGroup[2];
                int[] ids = Enumerable.Range(0, 21).Concat(new[] { 54 }).Concat(Enumerable.Range(24, 30)).ToArray();
                for (int ch = 0; ch < 2; ch++)
                {
                    var go = new GameObject("Character " + ch); go.transform.SetParent(root.transform);
                    var pose = go.AddComponent<TSMPNetworkHumanoidPoseSync>(); pose.autoResolveBones = false; pose.boneIds = ids; pose.networkId = (ushort)(201 + ch); pose.sendMode = SendMode.Always;
                    var bones = new Transform[ids.Length];
                    for (int b = 0; b < ids.Length; b++)
                    {
                        bones[b] = new GameObject("Bone " + ids[b]).transform; bones[b].SetParent(go.transform);
                        bones[b].localRotation = Quaternion.Euler(b * 1.3f, ch * 11f, b * 0.7f);
                    }
                    Set(pose, "_activeBoneCount", ids.Length); Set(pose, "_activeBoneIds", ids); Set(pose, "_activeBoneTargets", bones); Set(pose, "_rootMotionTarget", bones[0]);
                    groups[ch] = go.AddComponent<TSMPCharacterCrcGroup>(); groups[ch].pose = pose;
                    groups[ch].additionalTransforms = new TSMPNetworkTransformSync[2]; sources.Add(pose);
                    for (int t = 0; t < 2; t++)
                    {
                        var tg = new GameObject("Placement " + t); tg.transform.SetParent(go.transform); tg.transform.localPosition = new Vector3(ch + t * 0.2f, 1, 2);
                        var sync = tg.AddComponent<TSMPNetworkTransformSync>(); sync.networkId = (ushort)(101 + ch * 10 + t); sync.sendMode = SendMode.OnChange;
                        groups[ch].additionalTransforms[t] = sync; sources.Add(sync);
                    }
                }
                // An unprotected value represents face/other values outside the CRC groups.
                var other = root.AddComponent<TSMPNetworkTransformSync>(); other.networkId = 999; other.sendMode = SendMode.Always; sources.Add(other);
                var cache = new Dictionary<Type, TransSyncMetadata.Cache>(); var scheduler = new EncoderNativeSendState(); var writer = new EncoderCharacterCrcWriter();
                byte[] payload = null, encoded = null; int offset = 0, start = -1, variables = 0;
                for (int mode = 0; mode < 2; mode++)
                {
                    foreach (var group in groups) group.fingerProtection = (FingerProtection)mode;
                    for (int frame = 0; frame < 2; frame++)
                    {
                        int messages, vm, rpc, length; string error;
                        Require(EncoderNativeFrameBuilder.BuildNetworkPayload(sources, new List<EncoderNativeFrameBuilder.QueuedRpc>(), cache,
                            ref payload, ref encoded, ref offset, ref start, ref variables, (uint)frame, 4096, out messages, out vm, out rpc, out length, out error,
                            scheduler, frame / 30.0, 1f, null, writer, groups), error);
                        Require(CharacterCrcProtocol.TryLayout(encoded, length, out int m, out int chars, out int ranges), "Sender directory");
                        Require(chars == 2 && ranges == 4 && messages == 7, "All group members must be sent, including unchanged Transforms");
                        Require(CharacterCrcProtocol.ValidateEntries(encoded, m, chars, ranges), "Sender routing");
                        File.WriteAllBytes(Path.Combine(folder, "sender-" + mode + "-" + frame + ".bin"), encoded.Take(length).ToArray());
                        scheduler.Commit(payload, frame / 30.0);
                    }
                }
                // CRC off uses the legacy scheduler and has no directory.
                int mc, vc, rc, size; string failure;
                Require(EncoderNativeFrameBuilder.BuildNetworkPayload(sources, new List<EncoderNativeFrameBuilder.QueuedRpc>(), cache,
                    ref payload, ref encoded, ref offset, ref start, ref variables, 3, 4096, out mc, out vc, out rc, out size, out failure, scheduler, 1, 1f), failure);
                Require(!CharacterCrcProtocol.TryLayout(encoded, size, out _, out _, out _), "Legacy payload unexpectedly wrapped");
                File.WriteAllBytes(Path.Combine(folder, "legacy.bin"), encoded.Take(size).ToArray());
                Require(!EncoderNativeFrameBuilder.BuildNetworkPayload(sources, new List<EncoderNativeFrameBuilder.QueuedRpc>(), cache,
                    ref payload, ref encoded, ref offset, ref start, ref variables, 4, 100, out mc, out vc, out rc, out size, out failure, scheduler, 2, 1f, null, writer, groups), "Small capacity must not emit partial character");
                File.WriteAllText(Path.Combine(folder, "sender-result.txt"), "PASS: production capture, writer, unchanged Transform resend, legacy path, capacity rejection.");
            }
            finally { Object.DestroyImmediate(root); }
        }
#endif

        public static uint[] CpuFlags(byte[] packet)
        {
            uint[] flags = new uint[33], table = Crc32Runtime.EnsureTable(null);
            if (!CharacterCrcProtocol.TryLayout(packet, packet.Length, out int m, out int chars, out int ranges)) return flags;
            if (!CharacterCrcProtocol.MetadataCrc(packet, packet.Length, table, out uint crc) || crc != Binary.ReadUInt32LE(packet, m + 12)) return flags;
            flags[0] = CharacterCrcProtocol.ResultMagic | (uint)chars;
            for (int i = 0; i < chars; i++)
            {
                int d = m + 16 + i * 16, p = Binary.ReadUInt16LE(packet, d), length = Binary.ReadUInt16LE(packet, d + 2);
                uint body = 0xffffffffu, finger = 0xffffffffu;
                bool ok = CharacterCrcProtocol.PoseCrc(packet, p, length, false, table, ref body);
                int first = Binary.ReadUInt16LE(packet, d + 4), count = packet[d + 6];
                for (int r = first; r < first + count; r++)
                {
                    int rp = m + 16 + chars * 16 + r * 4;
                    body = CharacterCrcProtocol.Update(body, packet, Binary.ReadUInt16LE(packet, rp), Binary.ReadUInt16LE(packet, rp + 2), table);
                }
                flags[1 + 2 * i] = ok && (body ^ 0xffffffffu) == Binary.ReadUInt32LE(packet, d + 8) ? 1u : 0u;
                if (packet[d + 7] == 0) flags[2 + 2 * i] = 1u;
                else flags[2 + 2 * i] = CharacterCrcProtocol.PoseCrc(packet, p, length, true, table, ref finger) && (finger ^ 0xffffffffu) == Binary.ReadUInt32LE(packet, d + 12) ? 1u : 0u;
            }
            return flags;
        }

        public sealed class GpuCheck : IDisposable
        {
            public Material material; Texture2D input; RenderTexture output;
            public byte[] raw; public int resultOffset;
            public GpuCheck()
            {
                var shader = Shader.Find("Hidden/TSMP/Character CRC"); Require(shader != null && shader.isSupported, "CRC shader unavailable");
                material = new Material(shader);
            }
            public uint[] Run(byte[] packet, int prefix = 0, int width = 256)
            {
                int bytes = packet.Length + prefix, pixel = (bytes + 3) / 4, height = (pixel + 33 + width - 1) / width;
                if (input == null || input.width != width || input.height != height)
                {
                    if (input != null) Object.DestroyImmediate(input); if (output != null) { output.Release(); Object.DestroyImmediate(output); }
                    input = new Texture2D(width, height, TextureFormat.RGBA32, false, true); input.filterMode = FilterMode.Point;
                    output = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); output.filterMode = FilterMode.Point; output.Create();
                }
                var upload = new byte[width * height * 4]; Buffer.BlockCopy(packet, 0, upload, prefix, packet.Length); input.LoadRawTextureData(upload); input.Apply(false, false);
                material.SetInt("_InputWidth", width); material.SetInt("_OutputWidth", width); material.SetInt("_PayloadOffset", prefix); material.SetInt("_PayloadLength", packet.Length); material.SetInt("_ResultPixel", pixel);
                Graphics.Blit(input, output, material);
                var request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBA32); request.WaitForCompletion(); Require(!request.hasError, "GPU readback error");
                raw = request.GetData<byte>().ToArray(); resultOffset = pixel * 4;
                for (int i = 0; i < packet.Length; i++) Require(raw[prefix + i] == packet[i], "Copy mismatch " + i);
                uint[] flags = new uint[33]; for (int i = 0; i < flags.Length; i++) flags[i] = Binary.ReadUInt32LE(raw, resultOffset + i * 4);
                return flags;
            }
            public void Dispose() { if (input != null) Object.DestroyImmediate(input); if (output != null) { output.Release(); Object.DestroyImmediate(output); } if (material != null) Object.DestroyImmediate(material); }
        }

        public static IEnumerator ValidateGpu(string fixtureFolder, string reportFolder)
        {
            Directory.CreateDirectory(reportFolder);
            var table = Crc32Runtime.EnsureTable(null); var gold = System.Text.Encoding.ASCII.GetBytes("123456789");
            Require(Reference(gold) == 0xcbf43926u && Crc32Runtime.Compute(gold, 0, gold.Length, table) == 0xcbf43926u, "CRC known vector");
            int cases = 0;
            using (var gpu = new GpuCheck())
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    byte[] good = File.ReadAllBytes(Path.Combine(fixtureFolder, "sender-" + mode + "-0.bin"));
                    for (int bit = -1; bit < good.Length * 8; bit++)
                    {
                        byte[] packet = (byte[])good.Clone(); if (bit >= 0) packet[bit / 8] ^= (byte)(1 << (bit % 8));
                        uint[] expected = CpuFlags(packet), actual = gpu.Run(packet);
                        Require(expected[0] == actual[0], "Metadata mismatch mode=" + mode + " bit=" + bit);
                        if (expected[0] != 0)
                            for (int i = 1; i < 5; i++) Require(expected[i] == actual[i], "Group mismatch mode=" + mode + " bit=" + bit + " group=" + i);
                        cases++;
                        if (cases % 64 == 0) { File.WriteAllText(Path.Combine(reportFolder, "gpu-progress.txt"), cases.ToString()); yield return null; }
                    }
                    foreach (int prefix in new[] { 0, FrameHeader.Size }) foreach (int width in new[] { 127, 256 })
                    {
                        uint[] actual = gpu.Run(good, prefix, width); Require(actual[0] == (CharacterCrcProtocol.ResultMagic | 2u), "Packed readback layout"); cases++;
                    }
                    int m, chars, ranges; Require(CharacterCrcProtocol.TryLayout(good, good.Length, out m, out chars, out ranges), "fixture");
                    // A corrupt finger checksum must not invalidate common metadata or Body.
                    byte[] fingerChecksum = (byte[])good.Clone(); fingerChecksum[m + 16 + 12] ^= 1;
                    uint[] result = gpu.Run(fingerChecksum); Require(result[0] != 0 && result[1] == 1 && result[2] == (mode == 1 ? 0u : 1u), "Finger checksum isolation"); cases++;
                    foreach (int length in new[] { 0, 1, 27, good.Length - 1 })
                    { Require(gpu.Run(good.Take(length).ToArray())[0] == 0, "Truncated input accepted"); cases++; }
                }
            }
            File.WriteAllText(Path.Combine(reportFolder, "gpu-result.txt"), "PASS cases=" + cases + "; production shader versus CPU; every packet bit; exact byte copy; prefix/width; finger checksum isolation; truncation.");
        }
    }
}
#endif
