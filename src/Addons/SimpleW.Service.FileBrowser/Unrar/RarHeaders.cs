using System.Buffers.Binary;
using System.Text;


namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Describes one file header and its packed data range within a RAR volume.
    /// </summary>
    /// <param name="Name">Archived entry name, not yet validated as a destination path.</param>
    /// <param name="Size">Declared unpacked size of the complete logical entry in bytes.</param>
    /// <param name="Directory">Whether this header represents a directory.</param>
    /// <param name="Solid">Whether decoding continues the previous entry's compression state.</param>
    /// <param name="Generation">Legacy unpack version or the internal value 50 for RAR5.</param>
    /// <param name="Method">Compression method normalized so zero means stored data.</param>
    /// <param name="DictionarySize">Requested LZ dictionary size in bytes.</param>
    /// <param name="SplitBefore">Whether this entry begins in an earlier volume.</param>
    /// <param name="SplitAfter">Whether this entry continues into a later volume.</param>
    /// <param name="Crc">Optional CRC-32 of packed intermediate-part data or final unpacked entry data.</param>
    /// <param name="Hash">Optional BLAKE2sp digest of packed intermediate-part data or final unpacked entry data.</param>
    /// <param name="Range">Packed bytes supplied by this file header.</param>
    internal sealed record RarPart(string Name, long Size, bool Directory, bool Solid, int Generation,
        int Method, long DictionarySize, bool SplitBefore, bool SplitAfter, uint? Crc, byte[]? Hash, RarRange Range);

    /// <summary>
    /// Records the archive flags, volume sequence information and file parts read from one container.
    /// </summary>
    /// <param name="Format">Container format identifier: 4 for legacy RAR, or 5 for RAR5.</param>
    /// <param name="MultiVolume">Whether the archive declares a volume series.</param>
    /// <param name="Solid">Whether the main archive header declares solid compression.</param>
    /// <param name="First">Whether the header identifies this as the first volume.</param>
    /// <param name="Number">Zero-based volume number when supplied by the header parser.</param>
    /// <param name="Next">Whether the end header requests another volume.</param>
    /// <param name="HasEnd">Whether an end-of-archive header was encountered.</param>
    /// <param name="Parts">File headers and packed ranges in their container order.</param>
    internal sealed record RarVolumeInfo(int Format, bool MultiVolume, bool Solid, bool First, long? Number,
        bool Next, bool HasEnd, IReadOnlyList<RarPart> Parts);

    /// <summary>
    /// Parses bounded RAR4 and RAR5 headers without decompressing entry contents.
    /// </summary>
    internal static class RarHeaders {

        #region Signature and header reading

        /// <summary>
        /// Detects a RAR signature at the current position and restores that position; returns 4, 5 or zero.
        /// </summary>
        internal static int Signature(Stream input) {
            long position = input.Position;
            Span<byte> signature = stackalloc byte[8];
            int read = 0;
            while (read < 8) {
                int n = input.Read(signature[read..]);
                if (n == 0)
                    break;
                read += n;
            }
            input.Position = position;
            if (read >= 7 && signature[..6].SequenceEqual(new byte[] { 82, 97, 114, 33, 26, 7 })) {
                if (signature[6] == 0)
                    return 4;
                if (read == 8 && signature[6] == 1 && signature[7] == 0)
                    return 5;
            }
            return 0;
        }

        /// <summary>
        /// Reads a container's metadata, checks header integrity and rejects encrypted or unsupported entry types.
        /// </summary>
        internal static RarVolumeInfo Read(Stream input, RarLimits limits, CancellationToken cancellation) {
            input.Position = 0;
            int format = Signature(input);
            RarException.Require(format != 0, "Invalid RAR signature (SFX executables are not accepted).");
            input.Position = format == 4 ? 7 : 8;
            bool mainSeen = false, multi = false, solid = false, first = true, next = false, ended = false;
            long? number = null;
            var parts = new List<RarPart>();
            int headerCount = 0;
            while (input.Position < input.Length) {
                cancellation.ThrowIfCancellationRequested();
                if (++headerCount > limits.MaxEntries * 4L + 1024)
                    throw new RarException(RarFailure.Limit, "Too many RAR headers.");
                if (format == 4) {
                    byte[] prefix = Bytes(input, 7);
                    int length = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(5));
                    RarException.Require(length >= 7 && length <= limits.MaxHeaderBytes, "Invalid RAR4 header size.");
                    byte[] header = new byte[length];
                    prefix.CopyTo(header, 0);
                    Exact(input, header.AsSpan(7));
                    var c = new HeaderCursor(header);
                    ushort crc = c.U16();
                    int type = c.Byte(), flags = c.U16();
                    c.U16();
                    long packed = (flags & 0x8000) != 0 ? c.U32() : 0;
                    int crcEnd = header.Length;
                    if (type == 0x73) {
                        RarException.Require(!mainSeen && parts.Count == 0, "Unexpected RAR4 main header.");
                        c.Take(6);
                        if ((flags & 0x200) != 0)
                            c.Byte();
                        crcEnd = c.Position;
                        if ((flags & 0x80) != 0)
                            throw new RarException(RarFailure.Encrypted, "Encrypted RAR headers are not supported.");
                        multi = (flags & 1) != 0;
                        solid = (flags & 8) != 0;
                        first = (flags & 0x100) != 0 || !multi;
                        mainSeen = true;
                    }
                    else {
                        RarException.Require(mainSeen, "RAR file header precedes the main header.");
                        if (type is 0x74 or 0x7A) {
                            RarException.Require((flags & 0x8000) != 0, "RAR4 file header has no packed size.");
                            ulong size = c.U32();
                            int host = c.Byte();
                            uint checksum = c.U32();
                            c.U32(); // DOS time
                            int version = c.Byte(), method = c.Byte() - 0x30, nameLength = c.U16();
                            if (method != 0 && (version < 15 || version > 40))
                                throw RarException.Unsupported("Unknown RAR4 compression version.");
                            uint attributes = c.U32();
                            if ((flags & 0x100) != 0) {
                                ulong packed64 = (ulong)packed | (ulong)c.U32() << 32;
                                size |= (ulong)c.U32() << 32;
                                RarException.Require(packed64 <= long.MaxValue, "RAR packed size overflow.");
                                packed = (long)packed64;
                            }
                            RarException.Require(size <= long.MaxValue && size != uint.MaxValue, "Unknown RAR file size is not accepted.");
                            string name = LegacyName(c.Take(nameLength), (flags & 0x200) != 0, limits.LegacyNameEncoding);
                            if ((flags & (4 | 0x400)) != 0)
                                throw new RarException(RarFailure.Encrypted, "Encrypted RAR entries are not supported.");
                            if ((flags & 0x1000) != 0) {
                                int timeFlags = c.U16();
                                for (int t = 0; t < 4; t++) {
                                    int f = (timeFlags >> ((3 - t) * 4)) & 15;
                                    if ((f & 8) == 0)
                                        continue;
                                    if (t != 0)
                                        c.U32();
                                    c.Take(f & 3);
                                }
                            }
                            // Old embedded comments are outside the file-header checksum.
                            if (type == 0x74)
                                crcEnd = c.Position;
                            if (type == 0x74) {
                                RejectLink(host == 3, attributes);
                                bool directory = (flags & 0xE0) == 0xE0;
                                bool entrySolid = version < 20 ? solid && parts.Count > 0 : (flags & 16) != 0;
                                Add(parts, new(name, (long)size, directory, entrySolid, version, method,
                                    65536L << ((flags >> 5) & 7), (flags & 1) != 0, (flags & 2) != 0,
                                    checksum, null, new(input, input.Position, packed)), limits);
                            }
                        }
                        else if (type == 0x7B) {
                            next = (flags & 1) != 0;
                            if ((flags & 2) != 0)
                                c.U32();
                            if ((flags & 8) != 0)
                                number = c.U16();
                            ended = true;
                        }
                        else if (type is not (0x75 or 0x76 or 0x77 or 0x78 or 0x79) && (flags & 0x4000) == 0)
                            throw RarException.Unsupported("Unknown mandatory RAR4 header.");
                    }
                    RarException.Require((RarCrc.Compute(header.AsSpan(2, crcEnd - 2)) & 0xFFFF) == crc,
                        "RAR4 header checksum mismatch.");
                    SkipData(input, packed);
                }
                else {
                    uint crc = BinaryPrimitives.ReadUInt32LittleEndian(Bytes(input, 4));
                    var sizeBytes = new List<byte>(3);
                    ulong length = 0;
                    for (int i = 0; ; i++) {
                        RarException.Require(i < 3, "RAR5 header size encoding is too long.");
                        int b = input.ReadByte();
                        if (b < 0)
                            throw new RarException(RarFailure.Incomplete, "Missing RAR5 header size.");
                        sizeBytes.Add((byte)b);
                        length |= (ulong)(b & 127) << (7 * i);
                        if (b < 128)
                            break;
                    }
                    RarException.Require(length >= 2 && length <= (ulong)limits.MaxHeaderBytes, "Invalid RAR5 header size.");
                    byte[] header = Bytes(input, (int)length);
                    uint state = RarCrc.Update(uint.MaxValue, sizeBytes.ToArray());
                    RarException.Require(~RarCrc.Update(state, header) == crc, "RAR5 header checksum mismatch.");
                    var c = new HeaderCursor(header);
                    ulong type = c.Vint(), flags = c.Vint();
                    int extraSize = (flags & 1) != 0 ? c.Count() : 0;
                    long packed = (flags & 2) != 0 ? c.Size() : 0;
                    int extraStart = header.Length - extraSize;
                    RarException.Require(c.Position <= extraStart, "Invalid RAR5 extra area.");
                    if (type == 4)
                        throw new RarException(RarFailure.Encrypted, "Encrypted RAR headers are not supported.");
                    if (type == 1) {
                        RarException.Require(!mainSeen && parts.Count == 0, "Unexpected RAR5 main header.");
                        ulong archiveFlags = c.Vint();
                        multi = (archiveFlags & 1) != 0;
                        solid = (archiveFlags & 4) != 0;
                        number = (archiveFlags & 2) != 0 ? c.Size() : 0;
                        first = number == 0;
                        ValidateExtraFraming(header.AsSpan(extraStart).ToArray());
                        mainSeen = true;
                    }
                    else {
                        RarException.Require(mainSeen, "RAR file header precedes the main header.");
                        if (type is 2 or 3) {
                            ulong fileFlags = c.Vint();
                            long size = c.Size();
                            ulong attributes = c.Vint();
                            if ((fileFlags & 2) != 0)
                                c.U32();
                            uint? checksum = (fileFlags & 4) != 0 ? c.U32() : null;
                            ulong compression = c.Vint(), host = c.Vint();
                            string name = HeaderCursor.Utf8(c.Take(c.Count()));
                            RarException.Require(c.Position <= extraStart, "RAR5 filename overlaps extra records.");
                            byte[]? hash = Extras(header.AsSpan(extraStart).ToArray());
                            if (type == 2) {
                                if ((fileFlags & 8) != 0)
                                    throw RarException.Unsupported("Unknown unpacked RAR sizes are not supported.");
                                RarException.Require(host <= 1 && attributes <= uint.MaxValue, "Invalid RAR5 host attributes.");
                                RejectLink(host == 1, (uint)attributes);
                                int version = (int)(compression & 63);
                                int exponent = (int)((compression >> 10) & 31);
                                if (version != 0 || exponent > 15 || (compression >> 15) != 0)
                                    throw RarException.Unsupported("RAR compression versions after RAR5 are not supported.");
                                Add(parts, new(name, size, (fileFlags & 1) != 0, (compression & 64) != 0, 50,
                                    (int)((compression >> 7) & 7), 131072L << exponent,
                                    (flags & 8) != 0, (flags & 16) != 0, checksum, hash,
                                    new(input, input.Position, packed)), limits);
                            }
                        }
                        else if (type == 5) { next = (c.Vint() & 1) != 0; ended = true; }
                        else if ((flags & 4) == 0)
                            throw RarException.Unsupported("Unknown mandatory RAR5 header.");
                    }
                    RarException.Require(c.Position <= extraStart, "RAR5 fields overlap extra records.");
                    SkipData(input, packed);
                }
                if (ended)
                    break;
            }
            RarException.Require(mainSeen, "Missing RAR main header.");
            if (!ended && (format == 5 || (parts.Any(p => p.Generation >= 29) && parts.LastOrDefault()?.SplitAfter != true)))
                throw new RarException(RarFailure.Incomplete, "Missing RAR end header.");
            RarException.Require(!next || multi, "Non-volume RAR archive requests another volume.");
            return new(format, multi, solid, first, number, next, ended, parts);
        }


        #endregion

        #region Entry validation

        /// <summary>
        /// Validates basic entry metadata and per-volume limits before recording the packed part.
        /// </summary>
        private static void Add(List<RarPart> parts, RarPart part, RarLimits limits) {
            RarException.Require(part.Method is >= 0 and <= 5, "Invalid RAR compression method.");
            RarException.Require(part.Name.Length != 0 && !part.Name.Contains('\0'), "Invalid RAR filename.");
            RarException.Require(!part.Directory || (part.Size == 0 && part.Range.Length == 0 && !part.SplitBefore && !part.SplitAfter),
                "RAR directory contains data.");
            if (parts.Count >= limits.MaxEntries || part.Size > limits.MaxFileBytes)
                throw new RarException(RarFailure.Limit, "RAR entry quota exceeded.");
            parts.Add(part);
        }

        /// <summary>
        /// Rejects Unix links and special files or Windows reparse-point attributes.
        /// </summary>
        private static void RejectLink(bool unix, uint attributes) {
            uint kind = attributes & 0xF000;
            if (unix ? kind != 0 && kind != 0x8000 && kind != 0x4000 : (attributes & 0x400) != 0)
                throw RarException.Unsupported("RAR links, devices and reparse points are not supported.");
        }

        #endregion

        #region RAR5 extra records

        /// <summary>
        /// Reads RAR5 file extras, rejecting encryption and redirection and returning an optional BLAKE2sp digest.
        /// </summary>
        private static byte[]? Extras(byte[] extra) {
            var c = new HeaderCursor(extra);
            byte[]? hash = null;
            while (c.Remaining > 0) {
                int length = c.Count();
                var record = new HeaderCursor(c.Take(length).ToArray());
                ulong type = record.Vint();
                if (type == 1)
                    throw new RarException(RarFailure.Encrypted, "Encrypted RAR entries are not supported.");
                if (type == 5)
                    throw RarException.Unsupported("RAR links and redirections are not supported.");
                if (type == 2) {
                    if (record.Vint() != 0)
                        throw RarException.Unsupported("Unknown RAR content hash.");
                    RarException.Require(hash == null && record.Remaining == 32, "Invalid RAR BLAKE2sp record.");
                    hash = record.Take(32).ToArray();
                }
            }
            return hash;
        }

        /// <summary>
        /// Validates RAR5 archive extra-record boundaries while ignoring their optional metadata fields.
        /// </summary>
        private static void ValidateExtraFraming(byte[] extra) {
            var c = new HeaderCursor(extra);
            while (c.Remaining != 0) {
                int length = c.Count();
                var record = new HeaderCursor(c.Take(length).ToArray());
                record.Vint(); // Record type; the remaining fields are optional archive metadata.
            }
        }

        #endregion

        #region Bounded stream access

        /// <summary>
        /// Advances over a packed data area only if its complete declared range is present.
        /// </summary>
        private static void SkipData(Stream input, long size) {
            if (size < 0 || size > input.Length - input.Position)
                throw new RarException(RarFailure.Incomplete, "Truncated RAR data area.");
            input.Position += size;
        }

        /// <summary>
        /// Allocates and fills a header buffer of the requested length or reports incomplete input.
        /// </summary>
        private static byte[] Bytes(Stream input, int count) {
            var result = new byte[count];
            Exact(input, result);
            return result;
        }

        /// <summary>
        /// Fills a header span, translating premature end of stream into an incomplete-archive failure.
        /// </summary>
        private static void Exact(Stream input, Span<byte> bytes) {
            try { input.ReadExactly(bytes); }
            catch (EndOfStreamException) { throw new RarException(RarFailure.Incomplete, "Truncated RAR header."); }
        }

        #endregion

        #region Legacy filename decoding

        /// <summary>
        /// Decodes a RAR4 filename using its legacy encoding, UTF-8 or packed Unicode representation.
        /// </summary>
        private static string LegacyName(ReadOnlySpan<byte> data, bool unicode, Encoding fallbackEncoding) {
            if (!unicode)
                return fallbackEncoding.GetString(data);
            int zero = data.IndexOf((byte)0);
            if (zero < 0)
                return HeaderCursor.Utf8(data);
            byte[] fallback = data[..zero].ToArray();
            var c = new HeaderCursor(data[(zero + 1)..].ToArray());
            int high = c.Byte(), flags = 0, slots = 0;
            var result = new StringBuilder();
            while (c.Remaining > 0) {
                if (slots == 0) { flags = c.Byte(); slots = 4; }
                int mode = flags >> 6;
                flags = (flags << 2) & 255;
                slots--;
                if (mode == 0)
                    result.Append((char)c.Byte());
                else if (mode == 1)
                    result.Append((char)((high << 8) | c.Byte()));
                else if (mode == 2)
                    result.Append((char)c.U16());
                else {
                    int run = c.Byte();
                    bool corrected = (run & 128) != 0;
                    int correction = corrected ? c.Byte() : 0;
                    int count = (run & 127) + 2;
                    RarException.Require(count <= fallback.Length - result.Length, "Invalid RAR Unicode name run.");
                    while (count-- > 0) {
                        int low = (fallback[result.Length] + correction) & 255;
                        result.Append((char)(low | (corrected ? high << 8 : 0)));
                    }
                }
            }
            return result.ToString();
        }

        #endregion
    }

}