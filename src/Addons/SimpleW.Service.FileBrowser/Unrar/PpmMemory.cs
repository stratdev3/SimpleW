using System.Buffers.Binary;


namespace SimpleW.Service.FileBrowser.Unrar {

    // PPMd's allocator is part of the compressed format: exhaustion resets the model.
    // Offsets and 12-byte units keep that behavior independent of the CLR object layout.
    /// <summary>
    /// Allocates PPM contexts and symbol states in a fixed pool of 12-byte units.
    /// </summary>
    internal sealed class PpmMemory {

        #region Properties

        /// <summary>
        /// Gets the backing pool; offset zero is reserved as a null model reference.
        /// </summary>
        internal byte[] Data { get; }

        /// <summary>
        /// Gets or sets the next text-history offset used for deferred PPM successors.
        /// </summary>
        internal int Text { get; set; }

        /// <summary>
        /// Gets the boundary separating text-history space from model allocations.
        /// </summary>
        internal int Boundary { get; private set; }

        #endregion

        #region State

        /// <summary>
        /// Tracks the two allocation frontiers and the countdown to the next free-block coalescing pass.
        /// </summary>
        private int low, high, glue;

        /// <summary>
        /// Stores the head offset of each size class's linked list of free blocks.
        /// </summary>
        private readonly int[] free = new int[38];

        /// <summary>
        /// Maps size classes to their unit counts and requested unit counts back to size classes.
        /// </summary>
        private readonly int[] units = new int[38], bucket = new int[128];

        #endregion

        #region Construction

        /// <summary>
        /// Allocates the fixed pool, builds its size classes and initializes the allocation frontiers.
        /// </summary>
        internal PpmMemory(int bytes) {
            Data = new byte[checked(bytes + 4)];
            for (int i = 0, n = 0; i < 38; i++) {
                int step = i < 12 ? i / 4 + 1 : 4;
                for (int j = 0; j < step; j++)
                    bucket[n++] = i;
                units[i] = n;
            }
            Reset();
        }

        #endregion

        #region Pool lifecycle

        /// <summary>
        /// Discards all allocations and restores the initial split between text and model storage.
        /// </summary>
        internal void Reset() {
            Array.Clear(free);
            Text = 4;
            high = Data.Length;
            low = Boundary = high - ((Data.Length - 4) / 8 / 12) * 7 * 12;
            glue = 0;
        }

        #endregion

        #region Primitive memory access

        /// <summary>
        /// Reads a little-endian unsigned 16-bit value at the supplied pool address.
        /// </summary>
        internal ushort Word(int address) => BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(address, 2));

        /// <summary>
        /// Writes the low 16 bits of a value in little-endian order at the supplied pool address.
        /// </summary>
        internal void Word(int address, int value) => BinaryPrimitives.WriteUInt16LittleEndian(Data.AsSpan(address, 2), unchecked((ushort)value));

        /// <summary>
        /// Reads a little-endian signed 32-bit pool offset at the supplied pool address.
        /// </summary>
        internal int Pointer(int address) => BinaryPrimitives.ReadInt32LittleEndian(Data.AsSpan(address, 4));

        /// <summary>
        /// Writes a little-endian signed 32-bit pool offset at the supplied pool address.
        /// </summary>
        internal void Pointer(int address, int value) => BinaryPrimitives.WriteInt32LittleEndian(Data.AsSpan(address, 4), value);

        /// <summary>
        /// Copies bytes within the pool, allowing the source and destination ranges to overlap.
        /// </summary>
        internal void Copy(int source, int destination, int bytes) => Data.AsSpan(source, bytes).CopyTo(Data.AsSpan(destination, bytes));

        #endregion

        #region Allocation

        /// <summary>
        /// Allocates one 12-byte context, returning zero if the pool cannot supply it.
        /// </summary>
        internal int Context() {
            if (high - low >= 12) { high -= 12; return high; }
            return free[0] != 0 ? Pop(0) : Rare(0);
        }

        /// <summary>
        /// Allocates a rounded size class for the requested unit count, returning zero on exhaustion.
        /// </summary>
        internal int Allocate(int count) {
            int index = Index(count);
            if (free[index] != 0)
                return Pop(index);
            int bytes = units[index] * 12;
            if (high - low >= bytes) { int result = low; low += bytes; return result; }
            return Rare(index);
        }

        /// <summary>
        /// Resizes a unit allocation while preserving retained bytes, returning zero if growth cannot be
        /// satisfied.
        /// </summary>
        internal int Resize(int address, int oldCount, int newCount) {
            int before = Index(oldCount), after = Index(newCount);
            if (before == after)
                return address;
            if (newCount > oldCount) {
                int next = Allocate(newCount);
                if (next == 0)
                    return 0;
                Copy(address, next, oldCount * 12);
                Push(address, before);
                return next;
            }
            if (free[after] != 0) {
                int next = Pop(after);
                Copy(address, next, newCount * 12);
                Push(address, before);
                return next;
            }
            Split(address, before, after);
            return address;
        }

        /// <summary>
        /// Returns an allocation to the free list for its rounded unit count.
        /// </summary>
        internal void Free(int address, int count) => Push(address, Index(count));

        #endregion

        #region Free lists and coalescing

        /// <summary>
        /// Validates a request of one through 128 units and returns its size class.
        /// </summary>
        private int Index(int count) {
            RarException.Require(count is >= 1 and <= 128, "Invalid PPMd allocation size.");
            return bucket[count - 1];
        }

        /// <summary>
        /// Removes and returns the first block from a nonempty size-class free list.
        /// </summary>
        private int Pop(int index) {
            int address = free[index];
            free[index] = Pointer(address);
            return address;
        }

        /// <summary>
        /// Links a released block at the front of its size-class free list.
        /// </summary>
        private void Push(int address, int index) {
            Pointer(address, free[index]);
            free[index] = address;
        }

        /// <summary>
        /// Returns the unused tail of a larger block to appropriately sized free lists.
        /// </summary>
        private void Split(int address, int before, int after) {
            address += units[after] * 12;
            int remainder = units[before] - units[after];
            int index = Index(remainder);
            if (units[index] != remainder) {
                index--;
                int residue = remainder - units[index];
                Push(address + units[index] * 12, Index(residue));
            }
            Push(address, index);
        }

        /// <summary>
        /// Attempts coalescing, splitting or borrowing text space when ordinary allocation cannot succeed.
        /// </summary>
        private int Rare(int index) {
            if (glue == 0) {
                Coalesce();
                if (free[index] != 0)
                    return Pop(index);
            }
            for (int i = index + 1; i < free.Length; i++) {
                if (free[i] == 0)
                    continue;
                int address = Pop(i);
                Split(address, i, index);
                return address;
            }
            glue--;
            int bytes = units[index] * 12;
            if (Boundary - Text <= bytes)
                return 0;
            Boundary -= bytes;
            return Boundary;
        }

        /// <summary>
        /// Merges adjacent free blocks and rebuilds the free lists in the allocator's required traversal order.
        /// </summary>
        private void Coalesce() {
            glue = 255;
            // Preserve bucket/list traversal order while coalescing adjacent addresses.
            var order = new List<int>();
            var blocks = new Dictionary<int, int>();
            for (int i = 0; i < free.Length; i++)
                while (free[i] != 0) {
                    int p = Pop(i);
                    order.Add(p);
                    blocks.Add(p, units[i]);
                }
            order.Reverse();
            foreach (int p in order) {
                if (!blocks.TryGetValue(p, out int count))
                    continue;
                while (blocks.TryGetValue(p + count * 12, out int next) && count + next < 65536) {
                    blocks.Remove(p + count * 12);
                    count += next;
                }
                blocks[p] = count;
            }
            foreach (int initial in order) {
                if (!blocks.TryGetValue(initial, out int count))
                    continue;
                int address = initial;
                while (count > 128) { Push(address, 37); address += 1536; count -= 128; }
                int index = Index(count);
                if (units[index] != count) {
                    index--;
                    Push(address + units[index] * 12, Index(count - units[index]));
                }
                Push(address, index);
            }
        }

        #endregion
    }

}