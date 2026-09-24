namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Decodes RAR5 LZ blocks and schedules their standard output filters.
    /// </summary>
    internal sealed class Rar5Decoder {

        #region State

        /// <summary>
        /// Stores the literal/command, distance, low-distance alignment and repeat-length Huffman tables.
        /// </summary>
        private RarHuffman? main, distances, align, lengths;

        /// <summary>
        /// Stores the four most recently used match distances, newest first.
        /// </summary>
        private readonly long[] recent = new long[4];

        /// <summary>
        /// Stores the previous match length for the repeat-last-match command.
        /// </summary>
        private int lastLength;


        #endregion

        #region Entry decoding

        /// <summary>
        /// Decodes an entry's bounded blocks, validates block framing and preserves tables for solid successors.
        /// </summary>
        internal void Decode(Stream input, RarPart metadata, RarWindow window, CancellationToken cancellation) {
            if (!metadata.Solid) {
                main = distances = align = lengths = null;
                Array.Clear(recent);
                lastLength = 0;
            }
            var bits = new RarBits(input, cancellation);
            bool last;
            do {
                bits.Limit = long.MaxValue;
                bits.Align();
                int flags = (int)bits.Read(8), checksum = (int)bits.Read(8);
                int sizeBytes = ((flags >> 3) & 3) + 1;
                RarException.Require(sizeBytes <= 3, "Invalid RAR5 compressed-block size encoding.");
                int size = 0, check = 0x5A ^ flags ^ checksum;
                for (int i = 0; i < sizeBytes; i++) {
                    int b = (int)bits.Read(8);
                    size |= b << (8 * i);
                    check ^= b;
                }
                RarException.Require(check == 0 && size > 0, "Invalid RAR5 compressed-block header.");
                bits.Limit = checked(bits.Position + (size - 1L) * 8 + (flags & 7) + 1);
                last = (flags & 64) != 0;
                if ((flags & 128) != 0) {
                    byte[] codes = RarHuffman.ReadLengths(bits, 430);
                    main = new(codes.AsSpan(0, 306));
                    distances = new(codes.AsSpan(306, 64));
                    align = new(codes.AsSpan(370, 16));
                    lengths = new(codes.AsSpan(386, 44));
                }
                RarException.Require(main != null, "RAR5 block reuses missing Huffman tables.");
                while (bits.Position < bits.Limit) {
                    cancellation.ThrowIfCancellationRequested();
                    int symbol = main!.Decode(bits);
                    if (symbol < 256) { window.Literal((byte)symbol); continue; }
                    if (symbol == 256) {
                        uint offset = FilterInteger(bits), sizeValue = FilterInteger(bits);
                        RarException.Require(sizeValue is > 0 and <= 0x400000, "Invalid RAR5 filter size.");
                        int type = (int)bits.Read(3);
                        if (type > 3)
                            throw RarException.Unsupported("Unknown RAR5 filter type.");
                        int channels = type == 0 ? (int)bits.Read(5) + 1 : 0;
                        window.Schedule(new(checked(window.Produced + offset), (int)sizeValue,
                            (data, position) => RarFilters.Apply(type, data, position, channels, 0, false)), false);
                        continue;
                    }
                    if (symbol == 257) {
                        if (lastLength != 0)
                            window.Match(recent[0], lastLength);
                        continue;
                    }
                    long distance;
                    int length;
                    if (symbol < 262) {
                        int index = symbol - 258;
                        distance = recent[index];
                        for (int i = index; i > 0; i--)
                            recent[i] = recent[i - 1];
                        length = Length(lengths!.Decode(bits), bits);
                    }
                    else {
                        length = Length(symbol - 262, bits);
                        int slot = distances!.Decode(bits);
                        if (slot < 4)
                            distance = slot + 1;
                        else {
                            int n = (slot >> 1) - 1;
                            distance = 1 + ((2L | (slot & 1)) << n);
                            distance += n < 4 ? bits.Read(n) : ((long)bits.Read(n - 4) << 4) + align!.Decode(bits);
                        }
                        if (distance > 0x100)
                            length++;
                        if (distance > 0x2000)
                            length++;
                        if (distance > 0x40000)
                            length++;
                        for (int i = 3; i > 0; i--)
                            recent[i] = recent[i - 1];
                    }
                    recent[0] = distance;
                    lastLength = length;
                    window.Match(distance, length);
                }
            } while (!last);
            bits.Align();
            RarException.Require(input.Position == input.Length, "Trailing data after the final RAR5 compressed block.");
        }

        #endregion

        #region Encoded parameters

        /// <summary>
        /// Decodes a RAR5 match length from its slot and trailing extra bits.
        /// </summary>
        private static int Length(int slot, RarBits bits) {
            RarException.Require(slot is >= 0 and < 44, "Invalid RAR5 length slot.");
            if (slot < 8)
                return slot + 2;
            int n = (slot >> 2) - 1;
            return 2 + ((4 | (slot & 3)) << n) + (int)bits.Read(n);
        }

        /// <summary>
        /// Reads a filter parameter encoded as a two-bit byte count followed by little-endian bytes.
        /// </summary>
        private static uint FilterInteger(RarBits bits) {
            int bytes = (int)bits.Read(2) + 1;
            uint result = 0;
            for (int i = 0; i < bytes; i++)
                result |= bits.Read(8) << (8 * i);
            return result;
        }

        #endregion
    }

}