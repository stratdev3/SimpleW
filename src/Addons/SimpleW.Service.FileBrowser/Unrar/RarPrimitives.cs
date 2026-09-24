using System.Buffers.Binary;
using System.Text;


namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Classifies archive failures so callers can distinguish invalid data, unsupported features and limits.
    /// </summary>
    internal enum RarFailure {

        /// <summary>
        /// The archive contains invalid metadata, encoded data or a checksum mismatch.
        /// </summary>
        Corrupt,

        /// <summary>
        /// Required bytes, an entry continuation or an archive volume are missing.
        /// </summary>
        Incomplete,

        /// <summary>
        /// The archive contains encrypted headers or file data, which this reader refuses.
        /// </summary>
        Encrypted,

        /// <summary>
        /// Reading was requested from a part that requires an earlier volume.
        /// </summary>
        SecondaryVolume,

        /// <summary>
        /// The archive uses a format version or feature that this reader does not implement.
        /// </summary>
        Unsupported,

        /// <summary>
        /// An archive resource request exceeds a configured or declared bound.
        /// </summary>
        Limit,

        /// <summary>
        /// Extraction was requested out of order or after the decoder became unusable.
        /// </summary>
        InvalidSequence
    }

    /// <summary>
    /// Reports a RAR-specific failure together with a category usable by the host application.
    /// </summary>
    internal sealed class RarException : IOException {

        #region Properties

        /// <summary>
        /// Gets the failure category independently of the diagnostic message.
        /// </summary>
        internal RarFailure Failure { get; }

        #endregion

        #region Construction

        /// <summary>
        /// Creates an archive exception with a failure category and diagnostic message.
        /// </summary>
        internal RarException(RarFailure failure, string message) : base(message) => Failure = failure;

        #endregion

        #region Failure factories and guards

        /// <summary>
        /// Creates a corruption exception for invalid archive data or state.
        /// </summary>
        internal static RarException Corrupt(string message) => new(RarFailure.Corrupt, message);

        /// <summary>
        /// Creates an exception for an unsupported archive feature.
        /// </summary>
        internal static RarException Unsupported(string message) => new(RarFailure.Unsupported, message);

        /// <summary>
        /// Throws a corruption exception when an archive invariant does not hold.
        /// </summary>
        internal static void Require(bool condition, string message) {
            if (!condition)
                throw Corrupt(message);
        }

        #endregion
    }

    /// <summary>
    /// Defines the resource ceilings and legacy filename encoding used while reading and extracting RAR data.
    /// </summary>
    internal sealed record RarLimits {

        #region Properties

        /// <summary>
        /// Gets the maximum logical entry count and the per-volume file-header ceiling.
        /// </summary>
        internal int MaxEntries { get; init; } = 10_000;

        /// <summary>
        /// Gets the maximum number of source streams accepted for an archive.
        /// </summary>
        internal int MaxVolumes { get; init; } = 10_000;

        /// <summary>
        /// Gets the maximum accepted header size in bytes.
        /// </summary>
        internal int MaxHeaderBytes { get; init; } = 2 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum declared unpacked size of one entry in bytes.
        /// </summary>
        internal long MaxFileBytes { get; init; } = 10L * 1024 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum sum of declared unpacked entry sizes in bytes.
        /// </summary>
        internal long MaxTotalBytes { get; init; } = 50L * 1024 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum size in bytes of an LZ dictionary or a PPM model pool.
        /// </summary>
        internal int MaxDictionaryBytes { get; init; } = 256 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum number of pending filters or remembered legacy filter programs.
        /// </summary>
        internal int MaxFilters { get; init; } = 4096;

        /// <summary>
        /// Gets the encoding used for RAR4 names without Unicode metadata; defaults to Latin-1.
        /// </summary>
        internal Encoding LegacyNameEncoding { get; init; } = Encoding.Latin1;

        #endregion

        #region Configuration validation

        /// <summary>
        /// Rejects invalid resource ceilings or a missing legacy filename encoding before archive processing.
        /// </summary>
        internal void Validate() {
            if (MaxEntries < 1 || MaxVolumes < 1 || MaxHeaderBytes < 32 || MaxFileBytes < 0 ||
                MaxTotalBytes < 0 || MaxDictionaryBytes < 65536 || MaxFilters < 1 || LegacyNameEncoding == null)
                throw new ArgumentOutOfRangeException(nameof(RarLimits));
        }

        #endregion
    }

    // All parsers use bounded slices. No header-controlled count reaches an allocation unchecked.
    /// <summary>
    /// Reads bounded header fields and rejects truncated values or invalid encodings.
    /// </summary>
    /// <param name="bytes">Bounded header buffer to read.</param>
    /// <param name="position">Initial byte offset within the header buffer.</param>
    internal sealed class HeaderCursor(byte[] bytes, int position = 0) {

        #region Properties

        /// <summary>
        /// Gets the offset of the next field within the header buffer.
        /// </summary>
        internal int Position { get; private set; } = position;

        /// <summary>
        /// Gets the number of bytes left in the header buffer.
        /// </summary>
        internal int Remaining => bytes.Length - Position;

        #endregion

        #region Bounded field reading

        /// <summary>
        /// Reads one byte and rejects a truncated header.
        /// </summary>
        internal byte Byte() {
            RarException.Require(Remaining > 0, "Truncated RAR header field.");
            return bytes[Position++];
        }

        /// <summary>
        /// Returns a bounded slice of header bytes and advances past it.
        /// </summary>
        internal ReadOnlySpan<byte> Take(int count) {
            RarException.Require(count >= 0 && count <= Remaining, "RAR header field exceeds its boundary.");
            var result = bytes.AsSpan(Position, count);
            Position += count;
            return result;
        }

        /// <summary>
        /// Reads a little-endian unsigned 16-bit header field.
        /// </summary>
        internal ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        /// <summary>
        /// Reads a little-endian unsigned 32-bit header field.
        /// </summary>
        internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        /// <summary>
        /// Reads a base-128 unsigned variable-length integer and rejects values exceeding 64 bits.
        /// </summary>
        internal ulong Vint() {
            ulong value = 0;
            for (int i = 0; i < 10; i++) {
                byte b = Byte();
                RarException.Require(i != 9 || b <= 1, "RAR integer overflow.");
                value |= (ulong)(b & 127) << (i * 7);
                if (b < 128)
                    return value;
            }
            throw RarException.Corrupt("Invalid RAR integer.");
        }

        /// <summary>
        /// Reads a variable-length count bounded by the remaining header bytes.
        /// </summary>
        internal int Count() => CheckedCount(Vint(), Remaining);

        /// <summary>
        /// Reads a variable-length size that must fit in a nonnegative signed 64-bit value.
        /// </summary>
        internal long Size() {
            ulong n = Vint();
            RarException.Require(n <= long.MaxValue, "RAR size exceeds the supported range.");
            return (long)n;
        }

        #endregion

        #region Value validation and text decoding

        /// <summary>
        /// Converts an unsigned count to an integer after checking the supplied maximum.
        /// </summary>
        internal static int CheckedCount(ulong n, int maximum) {
            RarException.Require(n <= (ulong)maximum, "RAR field length exceeds its boundary.");
            return (int)n;
        }

        /// <summary>
        /// Decodes UTF-8 strictly, reporting invalid byte sequences as archive corruption.
        /// </summary>
        internal static string Utf8(ReadOnlySpan<byte> value) {
            try { return new UTF8Encoding(false, true).GetString(value); }
            catch (DecoderFallbackException) { throw RarException.Corrupt("Invalid UTF-8 filename."); }
        }

        #endregion
    }

    /// <summary>
    /// Calculates the reflected CRC-32 used by RAR headers, data and standard filter fingerprints.
    /// </summary>
    internal static class RarCrc {

        #region State

        /// <summary>
        /// Stores the lookup table for the reflected CRC-32 polynomial 0xEDB88320.
        /// </summary>
        private static readonly uint[] Table = CreateTable();

        #endregion

        #region CRC calculation

        /// <summary>
        /// Builds the 256-entry lookup table for byte-wise CRC updates.
        /// </summary>
        private static uint[] CreateTable() {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++) {
                uint v = i;
                for (int b = 0; b < 8; b++)
                    v = (v >> 1) ^ ((v & 1) == 0 ? 0 : 0xEDB88320U);
                table[i] = v;
            }
            return table;
        }

        /// <summary>
        /// Updates an unfinalized CRC accumulator; initialize it to all ones and complement it after the final
        /// update.
        /// </summary>
        internal static uint Update(uint state, ReadOnlySpan<byte> bytes) {
            foreach (byte b in bytes)
                state = (state >> 8) ^ Table[(state ^ b) & 255];
            return state;
        }

        /// <summary>
        /// Computes a complete CRC-32, including initialization and final complement.
        /// </summary>
        internal static uint Compute(ReadOnlySpan<byte> bytes) => ~Update(uint.MaxValue, bytes);

        #endregion
    }

    /// <summary>
    /// Reads bits most significant bit first, with cancellation and an optional compressed-block boundary.
    /// </summary>
    /// <param name="source">Readable packed-data stream, owned by the caller.</param>
    /// <param name="cancellation">Token checked when refilling from the source.</param>
    internal sealed class RarBits(Stream source, CancellationToken cancellation) {

        #region State

        /// <summary>
        /// Stores the current source byte and its number of unread low-order bits.
        /// </summary>
        private int current, remaining;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of consumed or skipped bits since this reader was created.
        /// </summary>
        internal long Position { get; private set; }

        /// <summary>
        /// Gets or sets the exclusive bit boundary enforced by reads of the current compressed block.
        /// </summary>
        internal long Limit { get; set; } = long.MaxValue;

        #endregion

        #region Bit reading and alignment

        /// <summary>
        /// Reads zero through 32 bits, rejecting block overruns and truncated input while checking cancellation on
        /// refill.
        /// </summary>
        internal uint Read(int count) {
            if ((uint)count > 32)
                throw new ArgumentOutOfRangeException(nameof(count));
            RarException.Require(Position <= Limit - count, "Compressed symbol crosses its block boundary.");
            uint value = 0;
            while (count > 0) {
                if (remaining == 0) {
                    cancellation.ThrowIfCancellationRequested();
                    current = source.ReadByte();
                    if (current < 0)
                        throw new RarException(RarFailure.Incomplete, "Truncated compressed RAR data.");
                    remaining = 8;
                }
                int n = Math.Min(count, remaining);
                remaining -= n;
                value = (value << n) | (uint)((current >> remaining) & ((1 << n) - 1));
                count -= n;
                Position += n;
            }
            return value;
        }

        /// <summary>
        /// Discards unread padding bits in the current byte and advances to the next byte boundary.
        /// </summary>
        internal void Align() {
            Position += remaining;
            remaining = 0;
        }

        #endregion
    }

    // Canonical Huffman decoding, MSB first. Incomplete alphabets are allowed, unused codes are not.
    /// <summary>
    /// Decodes canonical Huffman alphabets, allowing incomplete tables but rejecting unused codes.
    /// </summary>
    internal sealed class RarHuffman {

        #region State

        /// <summary>
        /// Stores counts, first codes and symbol offsets by bit length, plus symbols in canonical order.
        /// </summary>
        private readonly int[] counts = new int[16], starts = new int[16], offsets = new int[16], symbols;

        #endregion

        #region Construction

        /// <summary>
        /// Builds canonical decoding tables from lengths of at most 15 bits and rejects oversubscribed alphabets.
        /// </summary>
        internal RarHuffman(ReadOnlySpan<byte> lengths) {
            foreach (byte n in lengths) {
                RarException.Require(n <= 15, "Invalid Huffman code length.");
                if (n != 0)
                    counts[n]++;
            }
            int code = 0, offset = 0;
            for (int n = 1; n <= 15; n++) {
                code = (code + counts[n - 1]) << 1;
                starts[n] = code;
                offsets[n] = offset;
                offset += counts[n];
                RarException.Require(code + counts[n] <= 1 << n, "Oversubscribed Huffman alphabet.");
            }
            symbols = new int[offset];
            var next = (int[])offsets.Clone();
            for (int i = 0; i < lengths.Length; i++)
                if (lengths[i] != 0)
                    symbols[next[lengths[i]]++] = i;
        }

        #endregion

        #region Symbol and table decoding

        /// <summary>
        /// Reads one canonical code and returns its symbol, rejecting unused codes in incomplete alphabets.
        /// </summary>
        internal int Decode(RarBits bits) {
            int code = 0;
            for (int n = 1; n <= 15; n++) {
                code = (code << 1) | (int)bits.Read(1);
                int index = code - starts[n];
                if ((uint)index < counts[n])
                    return symbols[offsets[n] + index];
            }
            throw RarException.Corrupt("Invalid Huffman symbol.");
        }

        /// <summary>
        /// Decodes run-length encoded Huffman lengths, optionally applying deltas to the previous table.
        /// </summary>
        internal static byte[] ReadLengths(RarBits bits, int size, byte[]? previous = null) {
            var levelLengths = new byte[20];
            for (int i = 0; i < 20;) {
                byte n = (byte)bits.Read(4);
                if (n != 15) { levelLengths[i++] = n; continue; }
                int zeros = (int)bits.Read(4);
                if (zeros == 0) { levelLengths[i++] = 15; continue; }
                zeros += 2;
                RarException.Require(zeros <= 20 - i, "Invalid Huffman level run.");
                i += zeros;
            }
            var level = new RarHuffman(levelLengths);
            var lengths = new byte[size];
            for (int i = 0; i < size;) {
                int value = level.Decode(bits);
                if (value < 16) {
                    lengths[i] = (byte)((value + (previous?[i] ?? 0)) & 15);
                    i++;
                    continue;
                }
                bool repeat = value < 18;
                int run = (value & 1) == 0 ? 3 + (int)bits.Read(3) : 11 + (int)bits.Read(7);
                RarException.Require(run <= size - i && (!repeat || i > 0), "Invalid Huffman length run.");
                byte n = repeat ? lengths[i - 1] : (byte)0;
                lengths.AsSpan(i, run).Fill(n);
                i += run;
            }
            return lengths;
        }

        #endregion
    }

}