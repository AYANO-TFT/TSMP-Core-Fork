namespace K13A.TSMP
{
    // NetworkFrame bytes remain at offset zero. A versioned directory and footer follow.
    // All offsets are relative to that NetworkFrame, never to a readback texture.
    public static class CharacterCrcProtocol
    {
        public const int PayloadType = 0x0101;
        public const uint Magic = 0x43524354u; // TCRC
        public const uint ResultMagic = 0x43524300u;
        public const int MaxCharacters = 16;
        public const int MaxTransforms = 64;
        public const int HeaderBytes = 16;
        public const int CharacterBytes = 16;
        public const int TransformBytes = 4;
        public const int FooterBytes = 4;
        public const int ResultBytes = (MaxCharacters * 2 + 1) * 4;

        public static int DirectoryBytes(int characters, int transforms)
        {
            return HeaderBytes + characters * CharacterBytes + transforms * TransformBytes + FooterBytes;
        }

        public static bool TryLayout(byte[] bytes, int length, out int networkLength, out int characters, out int transforms)
        {
            networkLength = 0; characters = 0; transforms = 0;
            if (bytes == null || length < 28 || length > bytes.Length || length > 65535) return false;
            int m = Binary.ReadUInt16LE(bytes, length - 4);
            if ((m ^ Binary.ReadUInt16LE(bytes, length - 2)) != 65535 || m < 8 || m > length - 20) return false;
            if (Binary.ReadUInt32LE(bytes, m) != Magic || bytes[m + 4] != 1 || Binary.ReadUInt16LE(bytes, m + 8) != m || Binary.ReadUInt16LE(bytes, m + 10) != 0) return false;
            characters = bytes[m + 5]; transforms = Binary.ReadUInt16LE(bytes, m + 6);
            if (characters > MaxCharacters || transforms > MaxTransforms || m + DirectoryBytes(characters, transforms) != length) return false;
            int nextTransform = 0;
            for (int i = 0; i < characters; i++)
            {
                int d = m + HeaderBytes + i * CharacterBytes;
                int p = Binary.ReadUInt16LE(bytes, d), n = Binary.ReadUInt16LE(bytes, d + 2);
                int first = Binary.ReadUInt16LE(bytes, d + 4), count = bytes[d + 6];
                if (p < 8 || n < 4 || p > m - n || first != nextTransform || count > transforms - first || bytes[d + 7] > 1) return false;
                nextTransform += count;
                for (int j = 0; j < i; j++)
                    if (Binary.ReadUInt16LE(bytes, m + HeaderBytes + j * CharacterBytes) == p) return false;
            }
            if (nextTransform != transforms) return false;
            for (int i = 0; i < transforms; i++)
            {
                int d = m + HeaderBytes + characters * CharacterBytes + i * TransformBytes;
                int p = Binary.ReadUInt16LE(bytes, d), n = Binary.ReadUInt16LE(bytes, d + 2);
                if (p < 8 || n < 1 || p > m - n) return false;
                for (int j = 0; j < characters; j++)
                    if (Binary.ReadUInt16LE(bytes, m + HeaderBytes + j * CharacterBytes) == p) return false;
                for (int j = 0; j < i; j++)
                    if (Binary.ReadUInt16LE(bytes, m + HeaderBytes + characters * CharacterBytes + j * TransformBytes) == p) return false;
            }
            networkLength = m;
            return true;
        }

        public static uint Update(uint crc, byte[] bytes, int start, int count, uint[] table)
        {
            for (int i = start; i < start + count; i++) crc = (crc >> 8) ^ table[(int)((crc ^ bytes[i]) & 255u)];
            return crc;
        }

        // Protect routing and parsing fields but not unprotected values (e.g. expressions).
        // Group checksum words are deliberately excluded: a damaged finger checksum
        // must reject only Fingers, not the common directory and Body.
        public static bool MetadataCrc(byte[] bytes, int length, uint[] table, out uint crc)
        {
            crc = 0;
            int m, chars, ranges;
            if (!TryLayout(bytes, length, out m, out chars, out ranges)) return false;
            uint c = 0xffffffffu;
            c = Update(c, bytes, m, 12, table);
            for (int i = 0; i < chars; i++) c = Update(c, bytes, m + HeaderBytes + i * CharacterBytes, 8, table);
            c = Update(c, bytes, m + HeaderBytes + chars * CharacterBytes, ranges * TransformBytes + FooterBytes, table);
            int messages, cursor;
            if (!NetworkFrameReader.TryReadNetworkFrameHeader(bytes, out messages, out cursor)) return false;
            c = Update(c, bytes, 0, 8, table);
            for (int i = 0; i < messages; i++)
            {
                ushort id; int type, body, end, next;
                if (!NetworkFrameReader.TryReadMessageHeader(bytes, cursor, m, out id, out type, out body, out end, out next)) return false;
                c = Update(c, bytes, cursor, 8, table);
                int count, pos;
                if (type == NetworkFrameProtocol.MessageTypeVariableState)
                {
                    if (!NetworkFrameReader.TryReadVariableStateHeader(bytes, body, end, out count, out pos)) return false;
                    c = Update(c, bytes, body, 2, table);
                    for (int j = 0; j < count; j++)
                    {
                        uint hash; int vt, value, size, after;
                        if (!NetworkFrameReader.TryReadVariableEntry(bytes, pos, end, out hash, out vt, out value, out size, out after)) return false;
                        c = Update(c, bytes, pos, 7, table); pos = after;
                    }
                }
                else
                {
                    uint hash;
                    if (!NetworkFrameReader.TryReadRpcCallHeader(bytes, body, end, out hash, out count, out pos)) return false;
                    c = Update(c, bytes, body, 5, table);
                    for (int j = 0; j < count; j++)
                    {
                        int vt, value, size, after;
                        if (!NetworkFrameReader.TryReadRpcArgument(bytes, pos, end, out vt, out value, out size, out after)) return false;
                        c = Update(c, bytes, pos, 3, table); pos = after;
                    }
                }
                if (pos != end) return false;
                cursor = next;
            }
            if (cursor != m) return false;
            crc = c ^ 0xffffffffu;
            return true;
        }

        // CPU reference and sender implementation. Runtime receiver CRC is computed on GPU.
        public static bool PoseCrc(byte[] bytes, int start, int length, bool fingers, uint[] table, ref uint crc)
        {
            if (bytes == null || start < 0 || length < 4 || start > bytes.Length - length) return false;
            int version = bytes[start], flags = bytes[start + 1], bones = Binary.ReadUInt16LE(bytes, start + 2);
            if (version < 1 || version > 4 || bones > 55 || (flags & ~15) != 0) return false;
            int tail = start + 4 + bones * 6;
            if (tail > start + length) return false;
            if (!fingers) crc = Update(crc, bytes, start, 4, table);
            for (int i = 0; i < bones; i++)
            {
                int p = start + 4 + i * 6, id = bytes[p];
                if (id > 54) return false;
                if (!fingers) crc = Update(crc, bytes, p, 1, table);
                if (fingers == (id >= 24 && id <= 53)) crc = Update(crc, bytes, p + 1, 5, table);
            }
            if (!fingers) crc = Update(crc, bytes, tail, start + length - tail, table);
            return true;
        }

        // Preflight ALL entries before dispatching any event. Descriptor offsets must
        // point at actual RawBytes values, not at arbitrary positions in the packet.
        public static bool ValidateEntries(byte[] bytes, int m, int chars, int ranges)
        {
            int messages, cursor, matched = 0;
            if (!NetworkFrameReader.TryReadNetworkFrameHeader(bytes, out messages, out cursor)) return false;
            for (int i = 0; i < messages; i++)
            {
                ushort id; int type, body, end, next;
                if (!NetworkFrameReader.TryReadMessageHeader(bytes, cursor, m, out id, out type, out body, out end, out next)) return false;
                int count, pos;
                if (type == NetworkFrameProtocol.MessageTypeVariableState)
                {
                    if (!NetworkFrameReader.TryReadVariableStateHeader(bytes, body, end, out count, out pos)) return false;
                    for (int j = 0; j < count; j++)
                    {
                        uint hash; int vt, value, size, after;
                        if (!NetworkFrameReader.TryReadVariableEntry(bytes, pos, end, out hash, out vt, out value, out size, out after)) return false;
                        for (int k = 0; k < chars + ranges; k++)
                        {
                            int d = k < chars ? m + HeaderBytes + k * CharacterBytes : m + HeaderBytes + chars * CharacterBytes + (k - chars) * TransformBytes;
                            if (Binary.ReadUInt16LE(bytes, d) != value) continue;
                            if (count != 1 || value != body + 9 || vt != NetworkFrameProtocol.ValueTypeRawBytes || Binary.ReadUInt16LE(bytes, d + 2) != size) return false;
                            matched++;
                        }
                        pos = after;
                    }
                }
                else
                {
                    uint hash;
                    if (!NetworkFrameReader.TryReadRpcCallHeader(bytes, body, end, out hash, out count, out pos)) return false;
                    for (int j = 0; j < count; j++)
                    {
                        int vt, value, size, after;
                        if (!NetworkFrameReader.TryReadRpcArgument(bytes, pos, end, out vt, out value, out size, out after)) return false;
                        pos = after;
                    }
                }
                if (pos != end) return false;
                cursor = next;
            }
            return cursor == m && matched == chars + ranges;
        }
    }
}
