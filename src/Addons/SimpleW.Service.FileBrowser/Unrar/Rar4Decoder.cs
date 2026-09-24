namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Decodes RAR 2.9 through 4.x LZ and PPM blocks, retaining state for solid entries.
    /// </summary>
    /// <param name="limits">Resource ceilings for PPM memory and legacy filter programs.</param>
    internal sealed class Rar4Decoder(RarLimits limits) {

        #region State

        /// <summary>
        /// Stores the literal/command, distance, low-distance and repeat-length Huffman tables.
        /// </summary>
        private RarHuffman? main, distances, lowDistances, lengths;

        /// <summary>
        /// Retains the previous code lengths for delta-encoded Huffman table updates.
        /// </summary>
        private byte[] previousLengths = new byte[404];

        /// <summary>
        /// Stores the four most recently used match distances, newest first.
        /// </summary>
        private readonly int[] recent = new int[4];

        /// <summary>
        /// Tracks the last match length and the run state for repeated low-distance symbols.
        /// </summary>
        private int lastLength, lowRepeats, previousLow;

        /// <summary>
        /// Tracks whether the next block needs tables and whether the current block uses PPM decoding.
        /// </summary>
        private bool needsTables = true, ppmMode;

        /// <summary>
        /// Retains the PPM model while the compressed stream continues its existing state.
        /// </summary>
        private PpmDecoder ppm = new();

        /// <summary>
        /// Retains recognized filter programs for reuse by later blocks or solid entries.
        /// </summary>
        private readonly Rar4Filters filters = new(limits.MaxFilters);


        #endregion

        #region Entry decoding

        /// <summary>
        /// Decodes one packed entry into the LZ window, resetting model state only for a non-solid entry.
        /// </summary>
        internal void Decode(Stream input, RarPart metadata, RarWindow window, CancellationToken cancellation) {
            if (metadata.Generation is < 29 or > 40)
                throw RarException.Unsupported("This decoder requires the RAR 2.9-4.x compression algorithm.");
            if (!metadata.Solid) {
                previousLengths = new byte[404];
                Array.Clear(recent);
                lastLength = lowRepeats = previousLow = 0;
                needsTables = true;
                ppmMode = false;
                ppm = new();
                filters.Reset();
            }
            var bits = new RarBits(input, cancellation);
            if (needsTables)
                Tables(bits);
            while (true) {
                cancellation.ThrowIfCancellationRequested();
                if (ppmMode) {
                    byte value = ppm.Decode();
                    if (value != ppm.Escape) { window.Literal(value); continue; }
                    int action = ppm.Decode();
                    if (action == 0) { Tables(bits); continue; }
                    if (action == 2) { needsTables = true; break; }
                    if (action == 3) { filters.Read(ppm.Decode, window, cancellation); continue; }
                    if (action == 4) {
                        int distance = (ppm.Decode() << 16) | (ppm.Decode() << 8) | ppm.Decode();
                        window.Match(distance + 2, ppm.Decode() + 32);
                        continue;
                    }
                    if (action == 5) { window.Match(1, ppm.Decode() + 4); continue; }
                    window.Literal((byte)ppm.Escape);
                    continue;
                }
                int symbol = main!.Decode(bits);
                if (symbol < 256) { window.Literal((byte)symbol); continue; }
                if (symbol == 256) {
                    if (bits.Read(1) != 0) { Tables(bits); continue; }
                    needsTables = bits.Read(1) != 0;
                    break;
                }
                if (symbol == 257) { filters.Read(() => (byte)bits.Read(8), window, cancellation); continue; }
                if (symbol == 258) {
                    if (lastLength > 0)
                        window.Match(recent[0], lastLength);
                    continue;
                }
                int distanceValue, length;
                if (symbol < 263) {
                    int index = symbol - 259;
                    distanceValue = recent[index];
                    for (int i = index; i > 0; i--)
                        recent[i] = recent[i - 1];
                    length = Length(lengths!.Decode(bits), bits) + 2;
                }
                else if (symbol < 271) {
                    int slot = symbol - 263;
                    ReadOnlySpan<int> bases = [0, 4, 8, 16, 32, 64, 128, 192];
                    ReadOnlySpan<int> counts = [2, 2, 3, 4, 5, 6, 6, 6];
                    distanceValue = 1 + bases[slot] + (int)bits.Read(counts[slot]);
                    length = 2;
                    for (int i = 3; i > 0; i--)
                        recent[i] = recent[i - 1];
                }
                else {
                    length = Length(symbol - 271, bits) + 3;
                    int slot = distances!.Decode(bits), extra;
                    if (slot < 4) { distanceValue = slot + 1; extra = 0; }
                    else if (slot < 36) {
                        extra = (slot >> 1) - 1;
                        distanceValue = 1 + ((2 | (slot & 1)) << extra);
                    }
                    else if (slot < 48) { extra = 16; distanceValue = 1 + 262144 + (slot - 36) * 65536; }
                    else { extra = 18; distanceValue = 1 + 1048576 + (slot - 48) * 262144; }
                    if (slot > 9) {
                        distanceValue += (int)bits.Read(extra - 4) << 4;
                        if (lowRepeats > 0) { lowRepeats--; distanceValue += previousLow; }
                        else {
                            int low = lowDistances!.Decode(bits);
                            if (low == 16) { lowRepeats = 15; distanceValue += previousLow; }
                            else { previousLow = low; distanceValue += low; }
                        }
                    }
                    else
                        distanceValue += (int)bits.Read(extra);
                    if (distanceValue >= 0x2000)
                        length++;
                    if (distanceValue >= 0x40000)
                        length++;
                    for (int i = 3; i > 0; i--)
                        recent[i] = recent[i - 1];
                }
                recent[0] = distanceValue;
                lastLength = length;
                window.Match(distanceValue, length);
            }
        }

        #endregion

        #region Coding tables and lengths

        /// <summary>
        /// Reads a block's coding mode and initializes PPM parameters or the four LZ Huffman tables.
        /// </summary>
        private void Tables(RarBits bits) {
            bits.Align();
            if (bits.Read(1) != 0) {
                int flags = 128 | (int)bits.Read(7);
                ppm.Initialize(bits, flags, limits);
                ppmMode = true;
                return;
            }
            ppmMode = false;
            bool retain = bits.Read(1) != 0;
            previousLengths = RarHuffman.ReadLengths(bits, 404, retain ? previousLengths : null);
            main = new(previousLengths.AsSpan(0, 299));
            distances = new(previousLengths.AsSpan(299, 60));
            lowDistances = new(previousLengths.AsSpan(359, 17));
            lengths = new(previousLengths.AsSpan(376, 28));
            lowRepeats = previousLow = 0;
            needsTables = false;
        }

        /// <summary>
        /// Decodes a legacy length slot before the caller applies the command-specific base length.
        /// </summary>
        internal static int Length(int slot, RarBits bits) {
            RarException.Require(slot is >= 0 and < 28, "Invalid RAR4 length slot.");
            if (slot < 8)
                return slot;
            int n = (slot >> 2) - 1;
            return ((4 | (slot & 3)) << n) + (int)bits.Read(n);
        }

        #endregion
    }

}