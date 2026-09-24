namespace SimpleW.Service.FileBrowser.Unrar {

    // Original managed implementation of the variant-H model and the RAR range coder.
    /// <summary>
    /// Decodes the PPMd variant-H model used by RAR, retaining model state across solid blocks.
    /// </summary>
    internal sealed class PpmDecoder {

        #region Supporting types

        /// <summary>
        /// Adapts the secondary escape probability used when a PPM context has no matching symbol.
        /// </summary>
        private sealed class EscapeEstimate {

            #region State

            /// <summary>
            /// Stores the accumulated escape frequency, its scaling shift and the adaptation countdown.
            /// </summary>
            internal int Sum, Shift, Count;

            #endregion

            #region Probability adaptation

            /// <summary>
            /// Returns an escape estimate of at least one and subtracts the sampled mean from the accumulator.
            /// </summary>
            internal int Take() {
                int value = Sum >> Shift;
                Sum = (Sum - value) & 65535;
                return Math.Max(1, value);
            }

            /// <summary>
            /// Adjusts the estimate scaling when enough symbols have been decoded successfully.
            /// </summary>
            internal void Success() {
                if (Shift < 7 && --Count == 0) { Sum = (Sum * 2) & 65535; Count = 3 << Shift++; }
            }

            #endregion
        }

        #endregion

        #region State

        /// <summary>
        /// Contains the initial binary escape probabilities selected by suffix context group.
        /// </summary>
        private static readonly int[] InitialBinary = [0x3CDD, 0x1F3F, 0x59BF, 0x48F3, 0x64A1, 0x5ABC, 0x6632, 0x6051];

        /// <summary>
        /// Maps a binary escape probability group to its initial escape frequency.
        /// </summary>
        private static readonly int[] InitialEscape = [25, 14, 9, 7, 5, 5, 4, 4, 4, 3, 3, 3, 2, 2, 2, 2];

        /// <summary>
        /// Stores adaptive probabilities for single-symbol contexts, indexed by frequency and context features.
        /// </summary>
        private readonly int[,] binary = new int[128, 64];

        /// <summary>
        /// Stores secondary escape estimates for multi-symbol contexts.
        /// </summary>
        private readonly EscapeEstimate[,] estimates = new EscapeEstimate[25, 16];

        /// <summary>
        /// Provides the fixed-shift escape estimator used for the full root alphabet.
        /// </summary>
        private readonly EscapeEstimate rootEstimate = new() { Shift = 7, Count = 64 };

        /// <summary>
        /// Maps the number of available symbols to a secondary escape estimator row.
        /// </summary>
        private readonly int[] logarithm = new int[256];

        /// <summary>
        /// Holds the fixed pool containing the current PPM model and deferred successor text.
        /// </summary>
        private PpmMemory memory = null!;

        /// <summary>
        /// Supplies the encoded bytes consumed by the range decoder.
        /// </summary>
        private RarBits bits = null!;

        /// <summary>
        /// Tracks active contexts, the selected state, model order, suffix fallback and probability adaptation
        /// state.
        /// </summary>
        private int minimum, maximum, found, maxOrder, fall, run, initialRun, success, highBits, initialEscape;

        /// <summary>
        /// Stores the lower bound, interval width and current code of the unsigned range decoder.
        /// </summary>
        private uint low, range, code;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the decoded byte that introduces RAR control commands in PPM mode.
        /// </summary>
        internal int Escape { get; private set; } = 2;

        #endregion

        #region Construction

        /// <summary>
        /// Initializes symbol-count lookup tables and secondary escape estimators.
        /// </summary>
        internal PpmDecoder() {
            logarithm[1] = 1;
            logarithm[2] = 2;
            for (int i = 3, value = 3, left = 1; i < 256; i++) {
                logarithm[i] = value;
                if (--left == 0)
                    left = ++value - 2;
            }
            for (int row = 0; row < 25; row++)
                for (int col = 0; col < 16; col++)
                    estimates[row, col] = new();
        }

        #endregion

        #region Symbol decoding

        /// <summary>
        /// Reads PPM block parameters, optionally resets the model within its memory limit and initializes the
        /// range coder.
        /// </summary>
        internal void Initialize(RarBits input, int flags, RarLimits limits) {
            bits = input;
            bool reset = (flags & 32) != 0;
            int size = reset ? checked(((int)bits.Read(8) + 1) * 1024 * 1024) : 0;
            if ((flags & 64) != 0)
                Escape = (int)bits.Read(8);
            if (reset) {
                int order = (flags & 31) + 1;
                if (order > 16)
                    order = 16 + (order - 16) * 3;
                RarException.Require(order is >= 2 and <= 64, "Invalid PPMd order.");
                if (size > limits.MaxDictionaryBytes)
                    throw new RarException(RarFailure.Limit, "PPMd memory quota exceeded.");
                memory = new(size);
                maxOrder = order;
                Restart();
            }
            else
                RarException.Require(memory != null, "PPMd reuses an uninitialized model.");
            low = code = 0;
            range = uint.MaxValue;
            for (int i = 0; i < 4; i++)
                code = (code << 8) | bits.Read(8);
        }

        /// <summary>
        /// Decodes one symbol, following suffix contexts on escapes and adapting the model frequencies.
        /// </summary>
        internal byte Decode() {
            Span<bool> excluded = stackalloc bool[256];
            excluded.Clear();
            int count = Count(minimum), states = States(minimum);
            if (count > 1) {
                uint threshold = Threshold(Total(minimum));
                int cumulative = 0;
                for (int i = 0; i < count; i++) {
                    int state = states + i * 6, frequency = Frequency(state);
                    if (threshold < cumulative + frequency) {
                        Narrow(cumulative, frequency);
                        found = state;
                        byte symbol = Value(found);
                        if (i == 0) { success = 2 * frequency > Total(minimum) ? 1 : 0; run += success; }
                        else
                            success = 0;
                        Frequency(found, frequency + 4);
                        Total(minimum, Total(minimum) + 4);
                        if (i == 0) { if (Frequency(found) > 124) Rescale(); }
                        else if (Frequency(found) > Frequency(found - 6)) {
                            Swap(found, found - 6);
                            found -= 6;
                            if (Frequency(found) > 124)
                                Rescale();
                        }
                        Advance();
                        Normalize();
                        return symbol;
                    }
                    cumulative += frequency;
                    excluded[Value(state)] = true;
                }
                success = 0;
                highBits = Value(found) >= 64 ? 8 : 0;
                Narrow(cumulative, Total(minimum) - cumulative);
            }
            else {
                int frequency = Frequency(states), suffixCount = Count(Parent(minimum));
                highBits = Value(found) >= 64 ? 8 : 0;
                int group = suffixCount == 1 ? 0 : suffixCount == 2 ? 2 : suffixCount <= 11 ? 4 : 6;
                int column = success + group + highBits + (Value(states) >= 64 ? 16 : 0) + ((run >> 26) & 32);
                RarException.Require(frequency is >= 1 and <= 128, "Invalid PPMd binary frequency.");
                int probability = binary[frequency - 1, column];
                if (Threshold(16384) < probability) {
                    Narrow(0, probability);
                    binary[frequency - 1, column] = probability + 128 - ((probability + 32) >> 7);
                    found = states;
                    byte symbol = Value(found);
                    Frequency(found, frequency + (frequency < 128 ? 1 : 0));
                    success = 1;
                    run++;
                    Advance();
                    Normalize();
                    return symbol;
                }
                Narrow(probability, 16384 - probability);
                probability -= (probability + 32) >> 7;
                binary[frequency - 1, column] = probability;
                initialEscape = InitialEscape[probability >> 10];
                excluded[Value(states)] = true;
                success = 0;
            }
            for (int depth = 0; depth <= 64; depth++) {
                Normalize();
                int masked = Count(minimum);
                do {
                    fall++;
                    minimum = Parent(minimum);
                    RarException.Require(minimum != 0, "Unexpected PPMd end of model.");
                } while (Count(minimum) == masked);
                count = Count(minimum);
                states = States(minimum);
                int available = 0;
                for (int i = 0; i < count; i++)
                if (!excluded[Value(states + i * 6)])
                    available += Frequency(states + i * 6);
                EscapeEstimate estimate;
                int escape;
                if (count == 256) { estimate = rootEstimate; escape = 1; }
                else {
                    int unmasked = count - masked;
                    RarException.Require(unmasked > 0, "Invalid PPMd suffix statistics.");
                    uint difference = unchecked((uint)(Count(Parent(minimum)) - count));
                    int column = (unmasked < difference ? 1 : 0) + (Total(minimum) < 11 * count ? 2 : 0) +
                        (masked > unmasked ? 4 : 0) + highBits;
                    estimate = estimates[logarithm[unmasked - 1], column];
                    escape = estimate.Take();
                }
                int total = available + escape, cumulative = 0;
                uint threshold = Threshold(total);
                for (int i = 0; i < count; i++) {
                    int state = states + i * 6;
                    if (excluded[Value(state)])
                        continue;
                    int frequency = Frequency(state);
                    if (threshold < cumulative + frequency) {
                        Narrow(cumulative, frequency);
                        estimate.Success();
                        found = state;
                        byte symbol = Value(found);
                        Frequency(found, frequency + 4);
                        Total(minimum, Total(minimum) + 4);
                        if (Frequency(found) > 124)
                            Rescale();
                        run = initialRun;
                        Update();
                        Normalize();
                        return symbol;
                    }
                    cumulative += frequency;
                }
                Narrow(available, escape);
                estimate.Sum = (estimate.Sum + total) & 65535;
                for (int i = 0; i < count; i++)
                    excluded[Value(states + i * 6)] = true;
            }
            throw RarException.Corrupt("PPMd suffix chain exceeds its order.");
        }

        #endregion

        #region Model maintenance

        /// <summary>
        /// Resets the memory pool and rebuilds the uniform root model and initial probability tables.
        /// </summary>
        private void Restart() {
            memory.Reset();
            minimum = maximum = memory.Context();
            found = memory.Allocate(128);
            Count(minimum, 256);
            Total(minimum, 257);
            States(minimum, found);
            Parent(minimum, 0);
            for (int i = 0; i < 256; i++) { int s = found + i * 6; Value(s, (byte)i); Frequency(s, 1); Next(s, 0); }
            fall = maxOrder;
            run = initialRun = -Math.Min(maxOrder, 12) - 1;
            success = 0;
            for (int row = 0; row < 128; row++)
                for (int col = 0; col < 64; col++)
                    binary[row, col] = 16384 - InitialBinary[col & 7] / (row + 2);
            for (int row = 0; row < 25; row++)
                for (int col = 0; col < 16; col++) {
                    var e = estimates[row, col];
                    e.Sum = (5 * row + 10) << 3;
                    e.Shift = 3;
                    e.Count = 4;
                }
        }

        /// <summary>
        /// Moves to an existing successor context when possible, otherwise updating the model.
        /// </summary>
        private void Advance() {
            int next = Next(found);
            if (fall == 0 && next > memory.Text)
                minimum = maximum = next;
            else
                Update();
        }

        /// <summary>
        /// Adds the decoded symbol to context histories and restarts the model if its fixed pool is exhausted.
        /// </summary>
        private void Update() {
            byte symbol = Value(found);
            int frequency = Frequency(found), successor = Next(found);
            int parent = Parent(minimum);
            if (frequency < 31 && parent != 0) {
                int s = Find(parent, symbol);
                if (Count(parent) == 1) { if (Frequency(s) < 32) Frequency(s, Frequency(s) + 1); }
                else {
                    int first = States(parent);
                    if (s != first && Frequency(s) >= Frequency(s - 6)) { Swap(s, s - 6); s -= 6; }
                    if (Frequency(s) < 115) { Frequency(s, Frequency(s) + 2); Total(parent, Total(parent) + 2); }
                }
            }
            if (fall == 0) {
                int c = CreateSuccessors(true);
                if (c == 0) { Restart(); return; }
                Next(found, c);
                minimum = maximum = c;
                return;
            }
            memory.Data[memory.Text++] = symbol;
            int newSuccessor = memory.Text;
            if (memory.Text >= memory.Boundary) { Restart(); return; }
            if (successor != 0) {
                if (successor <= memory.Text) {
                    successor = CreateSuccessors(false);
                    if (successor == 0) { Restart(); return; }
                }
                if (--fall == 0) {
                    newSuccessor = successor;
                    if (maximum != minimum)
                        memory.Text--;
                }
            }
            else { Next(found, newSuccessor); successor = minimum; }
            int count = Count(minimum), baseline = Total(minimum) - count - frequency + 1;
            for (int c = maximum; c != minimum; c = Parent(c)) {
                RarException.Require(c != 0, "Invalid PPMd context chain.");
                int n = Count(c), states;
                if (n > 1) {
                    states = States(c);
                    if ((n & 1) == 0) {
                        states = memory.Resize(states, n / 2, n / 2 + 1);
                        if (states == 0) { Restart(); return; }
                        States(c, states);
                    }
                    Total(c, Total(c) + (2 * n < count ? 1 : 0) + (4 * n <= count && Total(c) <= 8 * n ? 2 : 0));
                }
                else {
                    states = memory.Allocate(1);
                    if (states == 0) { Restart(); return; }
                    memory.Copy(c + 2, states, 6);
                    int f = Frequency(states);
                    Frequency(states, f < 30 ? 2 * f : 120);
                    States(c, states);
                    Total(c, Frequency(states) + initialEscape + (count > 3 ? 1 : 0));
                }
                int factor = 2 * frequency * (Total(c) + 6), scale = baseline + Total(c), added;
                if (factor < 6 * scale) {
                    added = 1 + (factor > scale ? 1 : 0) + (factor >= 4 * scale ? 1 : 0);
                    Total(c, Total(c) + 3);
                }
                else {
                    added = 4 + (factor >= 9 * scale ? 1 : 0) + (factor >= 12 * scale ? 1 : 0) + (factor >= 15 * scale ? 1 : 0);
                    Total(c, Total(c) + added);
                }
                int appended = states + n * 6;
                Value(appended, symbol);
                Frequency(appended, added);
                Next(appended, newSuccessor);
                Count(c, n + 1);
            }
            minimum = maximum = successor;
        }

        /// <summary>
        /// Materializes deferred text successors as contexts, returning zero when allocation fails.
        /// </summary>
        private int CreateSuccessors(bool skip) {
            int branch = Next(found), c = minimum;
            Span<int> chain = stackalloc int[65];
            int length = 0;
            if (!skip)
                chain[length++] = found;
            while (Parent(c) != 0) {
                c = Parent(c);
                int s = Find(c, Value(found));
                if (Next(s) != branch) { c = Next(s); break; }
                RarException.Require(length < chain.Length, "PPMd successor chain exceeds its order.");
                chain[length++] = s;
            }
            if (length == 0)
                return c;
            RarException.Require(branch > 0 && branch <= memory.Text, "Invalid deferred PPMd successor.");
            byte symbol = memory.Data[branch];
            int frequency;
            if (Count(c) == 1)
                frequency = Frequency(c + 2);
            else {
                int f = Frequency(Find(c, symbol)) - 1, sum = Total(c) - Count(c) - f;
                RarException.Require(sum > 0, "Invalid PPMd inherited frequency.");
                frequency = 2 * f <= sum ? 1 + (5 * f > sum ? 1 : 0) : 1 + (2 * f + 3 * sum - 1) / (2 * sum);
            }
            while (length > 0) {
                int child = memory.Context();
                if (child == 0)
                    return 0;
                Count(child, 1);
                Value(child + 2, symbol);
                Frequency(child + 2, frequency);
                Next(child + 2, branch + 1);
                Parent(child, c);
                Next(chain[--length], child);
                c = child;
            }
            return c;
        }

        /// <summary>
        /// Halves and reorders context frequencies, removes zero-frequency states and shrinks their storage.
        /// </summary>
        private void Rescale() {
            int start = States(minimum), count = Count(minimum), oldCount = count;
            while (found != start) { Swap(found, found - 6); found -= 6; }
            int escape = Total(minimum) - Frequency(start), adder = fall == 0 ? 0 : 1;
            Frequency(start, (Frequency(start) + 4 + adder) >> 1);
            int sum = Frequency(start);
            for (int i = 1; i < count; i++) {
                int s = start + i * 6;
                escape -= Frequency(s);
                Frequency(s, (Frequency(s) + adder) >> 1);
                sum += Frequency(s);
                while (s > start && Frequency(s) > Frequency(s - 6)) { Swap(s, s - 6); s -= 6; }
            }
            while (count > 1 && Frequency(start + (count - 1) * 6) == 0) { count--; escape++; }
            if (count == 1) {
                int f = Frequency(start);
                do { f -= f >> 1; escape >>= 1; } while (escape > 1);
                Frequency(start, f);
                memory.Copy(start, minimum + 2, 6);
                memory.Free(start, (oldCount + 1) / 2);
                Count(minimum, 1);
                found = minimum + 2;
                return;
            }
            if (count != oldCount) {
                start = memory.Resize(start, (oldCount + 1) / 2, (count + 1) / 2);
                States(minimum, start);
                Count(minimum, count);
            }
            Total(minimum, sum + escape - (escape >> 1));
            found = start;
        }

        #endregion

        #region Range decoding

        /// <summary>
        /// Scales the coding range by the frequency total and returns the validated symbol threshold.
        /// </summary>
        private uint Threshold(int total) {
            RarException.Require(total > 0, "Invalid PPMd frequency total.");
            range /= (uint)total;
            RarException.Require(range != 0, "Invalid PPMd coding range.");
            uint result = unchecked(code - low) / range;
            RarException.Require(result < total, "PPMd symbol lies outside its coding range.");
            return result;
        }

        /// <summary>
        /// Restricts the coding interval to the selected symbol or escape frequency range.
        /// </summary>
        private void Narrow(int start, int size) {
            RarException.Require(start >= 0 && size > 0, "Invalid PPMd coding interval.");
            unchecked { low += (uint)start * range; range *= (uint)size; }
        }

        /// <summary>
        /// Refills the range code with encoded bytes until the interval has sufficient precision.
        /// </summary>
        private void Normalize() {
            unchecked {
                while (true) {
                    if ((low ^ (low + range)) >= 0x1000000) {
                        if (range >= 0x8000)
                            break;
                        range = (0U - low) & 0x7FFF;
                    }
                    code = (code << 8) | bits.Read(8);
                    range <<= 8;
                    low <<= 8;
                }
            }
        }

        #endregion

        #region Context and state access

        /// <summary>
        /// Finds a symbol state in a context or rejects an inconsistent suffix model.
        /// </summary>
        private int Find(int context, byte symbol) {
            int n = Count(context), s = States(context);
            for (int i = 0; i < n; i++, s += 6)
            if (Value(s) == symbol)
                return s;
            throw RarException.Corrupt("Missing PPMd suffix symbol.");
        }

        /// <summary>
        /// Exchanges two six-byte symbol states in the model pool.
        /// </summary>
        private void Swap(int a, int b) {
            for (int i = 0; i < 6; i++)
                (memory.Data[a + i], memory.Data[b + i]) = (memory.Data[b + i], memory.Data[a + i]);
        }

        /// <summary>
        /// Reads the number of symbol states in a validated context.
        /// </summary>
        private int Count(int c) {
            RarException.Require(c >= memory.Boundary && c <= memory.Data.Length - 12, "Invalid PPMd context reference.");
            int n = memory.Word(c);
            RarException.Require(n is >= 1 and <= 256, "Invalid PPMd context size.");
            return n;
        }

        /// <summary>
        /// Writes the number of symbol states in a context.
        /// </summary>
        private void Count(int c, int n) => memory.Word(c, n);

        /// <summary>
        /// Reads the total symbol and escape frequency of a multi-symbol context.
        /// </summary>
        private int Total(int c) => memory.Word(c + 2);

        /// <summary>
        /// Writes the total symbol and escape frequency of a multi-symbol context.
        /// </summary>
        private void Total(int c, int n) => memory.Word(c + 2, n);

        /// <summary>
        /// Reads the symbol-state offset, using inline storage for a single-symbol context.
        /// </summary>
        private int States(int c) => Count(c) == 1 ? c + 2 : memory.Pointer(c + 4);

        /// <summary>
        /// Writes the external symbol-state array offset of a multi-symbol context.
        /// </summary>
        private void States(int c, int s) => memory.Pointer(c + 4, s);

        /// <summary>
        /// Reads the suffix context offset.
        /// </summary>
        private int Parent(int c) => memory.Pointer(c + 8);

        /// <summary>
        /// Writes the suffix context offset.
        /// </summary>
        private void Parent(int c, int p) => memory.Pointer(c + 8, p);

        /// <summary>
        /// Reads the symbol byte in a six-byte state.
        /// </summary>
        private byte Value(int s) => memory.Data[s];

        /// <summary>
        /// Writes the symbol byte in a six-byte state.
        /// </summary>
        private void Value(int s, byte b) => memory.Data[s] = b;

        /// <summary>
        /// Reads the frequency byte in a six-byte state.
        /// </summary>
        private int Frequency(int s) => memory.Data[s + 1];

        /// <summary>
        /// Writes the frequency byte in a six-byte state.
        /// </summary>
        private void Frequency(int s, int n) => memory.Data[s + 1] = checked((byte)n);

        /// <summary>
        /// Reads the successor context or deferred text offset in a six-byte state.
        /// </summary>
        private int Next(int s) => memory.Pointer(s + 2);

        /// <summary>
        /// Writes the successor context or deferred text offset in a six-byte state.
        /// </summary>
        private void Next(int s, int n) => memory.Pointer(s + 2, n);

        #endregion
    }

}