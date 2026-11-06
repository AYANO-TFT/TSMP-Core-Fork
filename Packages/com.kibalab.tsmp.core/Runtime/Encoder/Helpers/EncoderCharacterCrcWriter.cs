#if !COMPILER_UDONSHARP
using System.Collections.Generic;
using K13A.TSMP.Udon;

namespace K13A.TSMP
{
    public sealed class EncoderCharacterCrcWriter
    {
        public readonly List<TSMPNetworkBehaviour> Members = new List<TSMPNetworkBehaviour>();
        private readonly TSMPCharacterCrcGroup[] _groups = new TSMPCharacterCrcGroup[16];
        private readonly int[] _poseOffsets = new int[16], _poseLengths = new int[16], _first = new int[16], _counts = new int[16];
        private readonly int[] _transformOffsets = new int[64], _transformLengths = new int[64];
        private int _characters, _transforms, _written, _writtenTransforms;
        private uint[] _table;

        public bool Prepare(TSMPCharacterCrcGroup[] groups, List<TSMPNetworkBehaviour> sources, out string error)
        {
            error = string.Empty; Members.Clear(); _characters = 0; _transforms = 0; _written = 0; _writtenTransforms = 0;
            if (groups == null) return true;
            foreach (TSMPCharacterCrcGroup group in groups)
            {
                if (group == null || !group.enabled || !group.gameObject.activeInHierarchy) continue;
                if (group.pose == null) { error = "CRC group has no Pose."; return false; }
                if (!group.pose.IsTSMPActive()) continue;
                if (_characters >= 16 || !sources.Contains(group.pose) || Members.Contains(group.pose) || (int)group.fingerProtection < 0 || (int)group.fingerProtection > 1)
                { error = "CRC group has duplicate/unbound Pose, unsupported finger mode, or too many characters."; return false; }
                Members.Add(group.pose);
                if (group.additionalTransforms != null)
                    foreach (TSMPNetworkTransformSync transform in group.additionalTransforms)
                    {
                        if (transform == null || !transform.IsTSMPActive() || !sources.Contains(transform) || Members.Contains(transform) || _transforms >= 64)
                        { error = "CRC Transform must be unique, active, bound to this encoder, and within the 64 Transform limit."; return false; }
                        Members.Add(transform); _transforms++;
                    }
                _groups[_characters++] = group;
            }
            return true;
        }

        public int ReservedBytes() { return CharacterCrcProtocol.DirectoryBytes(_characters, _transforms); }

        public bool WriteGroups(byte[] payload, int limit, ushort sequence, Dictionary<System.Type, TransSyncMetadata.Cache> cache,
            ref int offset, out int messages, out string error)
        {
            messages = 0; error = string.Empty;
            for (int i = 0; i < _characters; i++)
            {
                TSMPCharacterCrcGroup group = _groups[i];
                int count = group.additionalTransforms != null ? group.additionalTransforms.Length : 0;
                if (group.pose.poseBytes == null) { error = "CRC Pose was not captured."; return false; }
                int required = 17 + group.pose.poseBytes.Length;
                for (int j = 0; j < count; j++)
                {
                    byte[] value = group.additionalTransforms[j].packedBytes;
                    if (value == null) { error = "CRC Transform was not captured."; return false; }
                    required += 17 + value.Length;
                }
                // Never emit a partially protected character. A capacity problem is visible
                // instead of silently sending Pose without its placement transforms.
                if (required > limit - offset) { error = "CRC character snapshot exceeds payload capacity. Increase capacity or reduce active sources."; return false; }
                _groups[_written] = group; _first[_written] = _writtenTransforms; _counts[_written] = count;
                // Placement must be applied before animator-root-space Pose on the receiver.
                for (int j = 0; j < count; j++)
                {
                    TSMPNetworkTransformSync transform = group.additionalTransforms[j];
                    _transformOffsets[_writtenTransforms] = offset + 17; _transformLengths[_writtenTransforms++] = transform.packedBytes.Length;
                    if (!WriteValue(payload, ref offset, transform, "packedBytes", transform.packedBytes, sequence, cache))
                    { error = "Failed to write CRC Transform."; return false; }
                    messages++;
                }
                _poseOffsets[_written] = offset + 17; _poseLengths[_written] = group.pose.poseBytes.Length;
                if (!WriteValue(payload, ref offset, group.pose, "poseBytes", group.pose.poseBytes, sequence, cache))
                { error = "Failed to write CRC Pose."; return false; }
                messages++; _written++;
            }
            return true;
        }

        private static bool WriteValue(byte[] payload, ref int offset, TSMPNetworkBehaviour target, string fieldName,
            byte[] value, ushort sequence, Dictionary<System.Type, TransSyncMetadata.Cache> cache)
        {
            uint hash = 0; bool found = false;
            foreach (var field in TransSyncMetadata.GetOrCreate(cache, target.GetType()).Fields)
                if (field.FieldInfo.Name == fieldName) { hash = field.VariableHash; found = true; break; }
            if (!found) return false;
            int start = offset;
            int body = NetworkFrameWriter.BeginVariableState(payload, offset, BindingTable.ResolveNetworkId(target), sequence);
            if (body < 0) return false;
            offset = NetworkValueEntryWriter.WriteVariableValue(payload, body, hash, NetworkFrameProtocol.ValueTypeRawBytes, value);
            return offset >= 0 && NetworkFrameWriter.EndVariableState(payload, start, offset, 1);
        }

        public bool Finish(byte[] payload, ref int offset, int capacity, out string error)
        {
            error = string.Empty;
            int m = offset, length = m + CharacterCrcProtocol.DirectoryBytes(_written, _writtenTransforms);
            if (length > capacity || length > payload.Length || length > 65535) { error = "CRC directory exceeds payload capacity."; return false; }
            _table = Crc32Runtime.EnsureTable(_table);
            Binary.WriteUInt32LE(payload, m, CharacterCrcProtocol.Magic);
            payload[m + 4] = 1; payload[m + 5] = (byte)_written;
            Binary.WriteUInt16LE(payload, m + 6, (ushort)_writtenTransforms);
            Binary.WriteUInt16LE(payload, m + 8, (ushort)m); Binary.WriteUInt16LE(payload, m + 10, 0);
            for (int i = 0; i < _written; i++)
            {
                int d = m + 16 + i * 16;
                Binary.WriteUInt16LE(payload, d, (ushort)_poseOffsets[i]); Binary.WriteUInt16LE(payload, d + 2, (ushort)_poseLengths[i]);
                Binary.WriteUInt16LE(payload, d + 4, (ushort)_first[i]); payload[d + 6] = (byte)_counts[i]; payload[d + 7] = (byte)_groups[i].fingerProtection;
                uint body = 0xffffffffu, fingers = 0xffffffffu;
                if (!CharacterCrcProtocol.PoseCrc(payload, _poseOffsets[i], _poseLengths[i], false, _table, ref body)) { error = "Invalid Pose for CRC."; return false; }
                if (payload[d + 7] == 1 && !CharacterCrcProtocol.PoseCrc(payload, _poseOffsets[i], _poseLengths[i], true, _table, ref fingers)) { error = "Invalid finger Pose for CRC."; return false; }
                for (int j = _first[i]; j < _first[i] + _counts[i]; j++) body = CharacterCrcProtocol.Update(body, payload, _transformOffsets[j], _transformLengths[j], _table);
                Binary.WriteUInt32LE(payload, d + 8, body ^ 0xffffffffu); Binary.WriteUInt32LE(payload, d + 12, fingers ^ 0xffffffffu);
            }
            for (int i = 0; i < _writtenTransforms; i++)
            {
                int d = m + 16 + _written * 16 + i * 4;
                Binary.WriteUInt16LE(payload, d, (ushort)_transformOffsets[i]); Binary.WriteUInt16LE(payload, d + 2, (ushort)_transformLengths[i]);
            }
            Binary.WriteUInt16LE(payload, length - 4, (ushort)m); Binary.WriteUInt16LE(payload, length - 2, (ushort)(m ^ 65535));
            uint meta;
            if (!CharacterCrcProtocol.MetadataCrc(payload, length, _table, out meta)) { error = "Invalid NetworkFrame CRC structure."; return false; }
            Binary.WriteUInt32LE(payload, m + 12, meta); offset = length;
            return true;
        }
    }
}
#endif
