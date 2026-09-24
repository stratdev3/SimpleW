using System.Buffers.Binary;
using System.Numerics;


namespace SimpleW.Service.FileBrowser.Unrar {

    // BLAKE2sp: eight BLAKE2s leaf nodes, 64-byte stripes, one root node.
    // Original implementation of the BLAKE2 parameter block and RFC 7693 round function.
    /// <summary>
    /// Computes a 256-bit BLAKE2sp digest using eight striped BLAKE2s leaves and one root.
    /// </summary>
    internal sealed class RarBlake2sp {

        #region State

        /// <summary>
        /// Holds the eight BLAKE2s leaf nodes receiving successive 64-byte stripes.
        /// </summary>
        private readonly Node[] leaves = Enumerable.Range(0, 8).Select(i => new Node(i, false, i == 7)).ToArray();

        /// <summary>
        /// Buffers the stripe currently being assembled for a leaf.
        /// </summary>
        private readonly byte[] stripe = new byte[64];

        /// <summary>
        /// Tracks the buffered stripe length and the next leaf's index.
        /// </summary>
        private int used, leaf;

        /// <summary>
        /// Prevents updates or repeated finalization after the root digest has been produced.
        /// </summary>
        private bool finished;

        #endregion

        #region Incremental hashing

        /// <summary>
        /// Distributes input bytes across the leaf nodes in round-robin 64-byte stripes.
        /// </summary>
        internal void Update(ReadOnlySpan<byte> input) {
            if (finished)
                throw new InvalidOperationException("Hash already finalized.");
            while (!input.IsEmpty) {
                int count = Math.Min(64 - used, input.Length);
                input[..count].CopyTo(stripe.AsSpan(used));
                used += count;
                input = input[count..];
                if (used == 64) {
                    leaves[leaf].Update(stripe);
                    leaf = (leaf + 1) & 7;
                    used = 0;
                }
            }
        }

        /// <summary>
        /// Finalizes the remaining stripe and hashes the eight leaf digests into the root digest exactly once.
        /// </summary>
        internal byte[] Finish() {
            if (finished)
                throw new InvalidOperationException("Hash already finalized.");
            finished = true;
            if (used != 0)
                leaves[leaf].Update(stripe.AsSpan(0, used));
            var root = new Node(0, true, true);
            foreach (var node in leaves)
                root.Update(node.Finish());
            return root.Finish();
        }

        #endregion

        #region Supporting types

        /// <summary>
        /// Maintains one BLAKE2s leaf or root node in the BLAKE2sp hashing tree.
        /// </summary>
        private sealed class Node {

            #region State

            /// <summary>
            /// Contains the eight BLAKE2s initialization words.
            /// </summary>
            private static readonly uint[] Iv = [0x6A09E667, 0xBB67AE85, 0x3C6EF372, 0xA54FF53A,
            0x510E527F, 0x9B05688C, 0x1F83D9AB, 0x5BE0CD19];

            /// <summary>
            /// Contains the message-word permutations used by the ten BLAKE2s rounds.
            /// </summary>
            private static readonly byte[,] Order = {
            {0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15},
            {14,10,4,8,9,15,13,6,1,12,0,2,11,7,5,3},
            {11,8,12,0,5,2,15,13,10,14,3,6,7,1,9,4},
            {7,9,3,1,13,12,11,14,2,6,5,10,4,0,15,8},
            {9,0,5,7,2,4,10,15,14,1,11,12,6,8,3,13},
            {2,12,6,10,0,11,8,3,4,13,7,5,15,14,1,9},
            {12,5,1,15,14,13,4,10,0,7,6,3,9,2,8,11},
            {13,11,7,14,12,1,3,9,5,0,15,4,8,6,2,10},
            {6,15,14,9,11,3,0,8,12,2,13,7,1,4,10,5},
            {10,2,8,4,7,6,1,5,15,11,9,14,3,12,13,0} };

            /// <summary>
            /// Stores the node's eight chaining words after parameter initialization and block compression.
            /// </summary>
            private readonly uint[] h = (uint[])Iv.Clone();

            /// <summary>
            /// Buffers one 64-byte block, retaining the final block until finalization.
            /// </summary>
            private readonly byte[] block = new byte[64];

            /// <summary>
            /// Indicates whether this node is the last node at its tree depth.
            /// </summary>
            private readonly bool lastNode;

            /// <summary>
            /// Stores the number of input bytes buffered in the current block.
            /// </summary>
            private int used;

            /// <summary>
            /// Counts input bytes included in compressed blocks, including the final partial block at
            /// finalization.
            /// </summary>
            private ulong count;

            #endregion

            #region Construction

            /// <summary>
            /// Initializes a leaf or root using the BLAKE2sp tree parameters and its node offset.
            /// </summary>
            internal Node(int offset, bool root, bool lastNode) {
                this.lastNode = lastNode;
                h[0] ^= 0x02080020; // digest=32, key=0, fanout=8, depth=2
                h[2] ^= (uint)offset;
                h[3] ^= (root ? 1U << 16 : 0) | 32U << 24; // node depth and inner size
            }

            #endregion

            #region Node hashing

            /// <summary>
            /// Buffers node input and compresses full blocks once more input confirms they are not final.
            /// </summary>
            internal void Update(ReadOnlySpan<byte> input) {
                while (!input.IsEmpty) {
                    if (used == 64) { count += 64; Compress(false); used = 0; }
                    int n = Math.Min(64 - used, input.Length);
                    input[..n].CopyTo(block.AsSpan(used));
                    used += n;
                    input = input[n..];
                }
            }

            /// <summary>
            /// Compresses the padded final block and serializes the node's 32-byte digest.
            /// </summary>
            internal byte[] Finish() {
                count += (uint)used;
                block.AsSpan(used).Clear();
                Compress(true);
                byte[] digest = new byte[32];
                for (int i = 0; i < 8; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(digest.AsSpan(i * 4), h[i]);
                return digest;
            }

            #endregion

            #region Compression rounds

            /// <summary>
            /// Applies ten BLAKE2s rounds with the byte counter and final-block and final-node flags.
            /// </summary>
            private void Compress(bool last) {
                Span<uint> v = stackalloc uint[16];
                Span<uint> m = stackalloc uint[16];
                h.CopyTo(v);
                Iv.CopyTo(v[8..]);
                v[12] ^= (uint)count;
                v[13] ^= (uint)(count >> 32);
                if (last) { v[14] = ~v[14]; if (lastNode) v[15] = ~v[15]; }
                for (int i = 0; i < 16; i++)
                    m[i] = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(i * 4));
                for (int r = 0; r < 10; r++) {
                    Mix(v, 0, 4, 8, 12, m[Order[r, 0]], m[Order[r, 1]]);
                    Mix(v, 1, 5, 9, 13, m[Order[r, 2]], m[Order[r, 3]]);
                    Mix(v, 2, 6, 10, 14, m[Order[r, 4]], m[Order[r, 5]]);
                    Mix(v, 3, 7, 11, 15, m[Order[r, 6]], m[Order[r, 7]]);
                    Mix(v, 0, 5, 10, 15, m[Order[r, 8]], m[Order[r, 9]]);
                    Mix(v, 1, 6, 11, 12, m[Order[r, 10]], m[Order[r, 11]]);
                    Mix(v, 2, 7, 8, 13, m[Order[r, 12]], m[Order[r, 13]]);
                    Mix(v, 3, 4, 9, 14, m[Order[r, 14]], m[Order[r, 15]]);
                }
                for (int i = 0; i < 8; i++)
                    h[i] ^= v[i] ^ v[i + 8];
            }

            /// <summary>
            /// Applies the BLAKE2s add, XOR and rotate mixing function to four working words.
            /// </summary>
            private static void Mix(Span<uint> v, int a, int b, int c, int d, uint x, uint y) {
                unchecked {
                    v[a] += v[b] + x;
                    v[d] = BitOperations.RotateRight(v[d] ^ v[a], 16);
                    v[c] += v[d];
                    v[b] = BitOperations.RotateRight(v[b] ^ v[c], 12);
                    v[a] += v[b] + y;
                    v[d] = BitOperations.RotateRight(v[d] ^ v[a], 8);
                    v[c] += v[d];
                    v[b] = BitOperations.RotateRight(v[b] ^ v[c], 7);
                }
            }

            #endregion
        }

        #endregion
    }

    /// <summary>
    /// Writes decoded bytes while enforcing the declared size and accumulating the entry checksum.
    /// </summary>
    /// <param name="output">Writable destination owned by the caller.</param>
    /// <param name="metadata">Declared unpacked size and expected checksum of the logical entry.</param>
    /// <param name="cancellation">Token checked before writing each block.</param>
    internal sealed class RarCheckedOutput(Stream output, RarPart metadata, CancellationToken cancellation) {

        #region State

        /// <summary>
        /// Stores the unfinalized CRC-32 accumulator for emitted bytes.
        /// </summary>
        private uint crc = uint.MaxValue;

        /// <summary>
        /// Accumulates BLAKE2sp when the entry declares that digest.
        /// </summary>
        private readonly RarBlake2sp? hash = metadata.Hash == null ? null : new();

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of decoded bytes successfully written to the destination.
        /// </summary>
        internal long Count { get; private set; }

        #endregion

        #region Output and integrity

        /// <summary>
        /// Checks cancellation and the declared size before writing bytes and updating integrity accumulators.
        /// </summary>
        internal void Write(ReadOnlySpan<byte> data) {
            cancellation.ThrowIfCancellationRequested();
            if (data.Length > metadata.Size - Count)
                throw new RarException(RarFailure.Limit, "Decoded RAR data exceeds the declared size.");
            output.Write(data);
            crc = RarCrc.Update(crc, data);
            hash?.Update(data);
            Count += data.Length;
        }

        /// <summary>
        /// Checks the exact unpacked size and the BLAKE2sp digest, or CRC when no BLAKE2sp digest is declared.
        /// </summary>
        internal void Complete() {
            RarException.Require(Count == metadata.Size, "RAR unpacked size mismatch.");
            if (hash != null)
                RarException.Require(hash.Finish().AsSpan().SequenceEqual(metadata.Hash), "RAR BLAKE2sp mismatch.");
            else if (metadata.Crc.HasValue)
                RarException.Require(~crc == metadata.Crc.Value, "RAR data CRC mismatch.");
        }

        #endregion
    }

}