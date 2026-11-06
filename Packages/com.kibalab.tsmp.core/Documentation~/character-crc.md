# Character CRC v1

The standalone encoder can send `PayloadType = 0x0101`. The shared decoder accepts this format and legacy NetworkFrame `0x0100` in both standalone Unity and Udon. Keep CRC off when using a legacy receiver. The usual outer FrameHeader and its CRC remain unchanged. The inner 32-bit NetworkFrame sequence must equal the outer frame index.

## Wire layout

All integers are little endian. Offsets are relative to the first NetworkFrame byte. The unmodified NetworkFrame is followed by a directory, then a four-byte footer. The directory is not a NetworkFrame message.

| Region | Offset | Meaning |
|---|---:|---|
| Directory header, 16 bytes | 0 | uint32 magic `0x43524354` |
| | 4 | uint8 version = 1 |
| | 5 | uint8 character count, at most 16 |
| | 6 | uint16 additional Transform count, at most 64 |
| | 8 | uint16 NetworkFrame byte length |
| | 10 | uint16 reserved = 0 |
| | 12 | uint32 metadata CRC |
| Character descriptor, 16 bytes each | 0, 2 | uint16 Pose value offset and length |
| | 4 | uint16 first additional Transform descriptor index |
| | 6 | uint8 additional Transform count |
| | 7 | uint8 finger mode: 0 excluded, 1 separate |
| | 8, 12 | uint32 Body CRC and Finger CRC |
| Transform descriptor, 4 bytes each | 0, 2 | uint16 value offset and length |
| Footer, 4 bytes | 0, 2 | uint16 NetworkFrame length and its bitwise complement |

Transform descriptor ranges are consecutive, non-overlapping, and exhaust the descriptor array. Value offsets are unique. Every protected member is a single RawBytes variable in its own VariableState message. Additional Transforms precede their Pose in the NetworkFrame. Group members bypass the change-only scheduler, so each emitted snapshot contains all members. Capacity failure aborts that frame instead of emitting a partial character.

Directory overhead is `20 + 16*characters + 4*transforms` bytes. Two characters with two Transforms each use 68 bytes in either finger mode. CRC exclusion changes coverage, not the transmitted Pose values. Inactive groups are omitted. The on-wire limit applies to active characters, not the number of configured character choices.

## CRC coverage and ordering

CRC-32/ISO-HDLC: reflected polynomial `0xEDB88320`, initial value `0xFFFFFFFF`, final XOR `0xFFFFFFFF`; `123456789` produces `0xCBF43926`.

Metadata CRC consumes, in order:

1. Directory header bytes 0..11.
2. Bytes 0..7 of each character descriptor, in descriptor order.
3. All Transform descriptors, then the footer.
4. NetworkFrame header, then every message header and its variable/RPC entry headers in message order. Value bytes are skipped.

All value ranges are bounds checked before dispatch. Group checksum words are excluded from metadata coverage: damage to a Finger CRC must not invalidate Body.

Body CRC consumes the Pose header, then each bone ID and non-finger rotation bytes, then the root tail, then all additional Transform values in descriptor order. Finger IDs are Humanoid IDs 24..53 inclusive. Finger CRC consumes only those records' five rotation bytes in Pose order. All bone IDs remain Body-protected because they determine how rotations are interpreted. Excluded mode ignores the Finger CRC word (the sender writes zero).

Metadata failure rejects every message in the frame. Body failure holds the whole character group, including fingers and placement. Finger-only failure updates Body and preserves the previous local finger interpolation targets, including when parent wrists move. A later valid snapshot resumes updates. A checksum failure does not attempt repair.

## GPU receiver

`TSMPCharacterCrc.shader` uses a fragment pass and a 16-entry nibble CRC table. Each shader invocation caches the loaded RGBA pixel and consumes its four byte channels before loading another pixel. The pass copies the byte payload and appends 33 uint result pixels: metadata magic/count, followed by Body/Finger flags for up to 16 characters. CPU parsing also verifies descriptor membership and frame sequence before any value dispatch.

Results share the decoder's existing asynchronous readback. The two in-flight slots own separate CRC output textures and result offsets. Readback rows can increase when padding is insufficient. CRC does not add a second readback round trip. No source-video resolution or BlockSize change is required.

The same repository and branch contain the standalone CRC sender and the Unity/Udon receiver. The native encoder path is conditionally compiled; Udon reception does not execute the sender's CRC writer. Target groups are configured on the standalone sender only. Keep source files and asset GUIDs identical across working copies, and update Udon program assets in the VRC project. The RIOT diagnostics panel counts CRC-checked samples independently from the existing frame-loss statistic. With normal duplicate suppression enabled, repeating a rejected metadata frame does not inflate its count.

## Validation entry points

`Editor/Tests/CharacterCrc/CharacterCrcValidation.ExportSenderFixtures` generates fixtures using production capture and serialization. `ValidateGpu` compares production GPU flags against CPU CRCs with each packet bit flipped separately. `CharacterCrcReceiverValidation.Run` exercises the real Luma4 raster, decoder, async readback, routing, interpolation targets, and legacy transitions. Enumerators must be driven with nested enumerators supported by the caller.

These Editor checks do not establish production VRChat FPS, video-compression failure rates, or cross-GPU compatibility. Changing directory structure or CRC coverage requires a version/format decision and matching sender/receiver tests.
