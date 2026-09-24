namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Describes an output block transformation at an absolute position in the unfiltered LZ stream.
    /// </summary>
    /// <param name="Start">Absolute start in the unfiltered stream since the dictionary reset.</param>
    /// <param name="Length">Number of input bytes transformed without changing the block length.</param>
    /// <param name="Transform">Transformation receiving a block copy and its byte offset within the current file.</param>
    internal sealed record RarFilter(long Start, int Length, Func<byte[], long, byte[]> Transform);

    // Keeps original LZ bytes: post-processing must never alter the match dictionary.
    /// <summary>
    /// Maintains the original LZ history and emits filtered bytes without altering that history.
    /// </summary>
    internal sealed class RarWindow {

        #region State

        /// <summary>
        /// Stores the original, unfiltered bytes in a circular LZ history buffer.
        /// </summary>
        private byte[] dictionary = [];

        /// <summary>
        /// Queues output transformations in order of their absolute dictionary positions.
        /// </summary>
        private readonly Queue<RarFilter> filters = new();

        /// <summary>
        /// Receives filtered bytes and verifies the current entry's size and checksum.
        /// </summary>
        private RarCheckedOutput output = null!;

        /// <summary>
        /// Cancels dictionary production and output processing for the current entry.
        /// </summary>
        private CancellationToken cancellation;

        /// <summary>
        /// Tracks the emitted position, entry start, declared entry length and next flush threshold.
        /// </summary>
        private long flushed, fileStart, expected, nextFlush;

        /// <summary>
        /// Limits the number of output transformations waiting for their input bytes.
        /// </summary>
        private int maxFilters;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the total raw bytes produced since the dictionary was last reset.
        /// </summary>
        internal long Produced { get; private set; }

        /// <summary>
        /// Gets the raw bytes produced for the current entry.
        /// </summary>
        internal long FileProduced => Produced - fileStart;

        #endregion

        #region Entry lifecycle

        /// <summary>
        /// Starts an entry's output state, retaining a compatible dictionary for solid data or resetting it
        /// otherwise.
        /// </summary>
        internal void Begin(RarPart metadata, RarCheckedOutput destination, RarLimits limits, CancellationToken token) {
            long size = Math.Max(65536, metadata.DictionarySize);
            if (size > limits.MaxDictionaryBytes)
                throw new RarException(RarFailure.Limit, "RAR dictionary exceeds the memory limit.");
            if (metadata.Solid)
                RarException.Require(dictionary.Length == size, "Incompatible dictionary in a solid RAR sequence.");
            else {
                dictionary = new byte[(int)size];
                Produced = 0;
            }
            filters.Clear();
            flushed = fileStart = Produced;
            nextFlush = Produced + 65536;
            expected = metadata.Size;
            output = destination;
            cancellation = token;
            maxFilters = limits.MaxFilters;
        }

        #endregion

        #region Dictionary production

        /// <summary>
        /// Appends one raw byte to history, flushing pending output before the circular buffer can overwrite it.
        /// </summary>
        internal void Literal(byte b) {
            if ((Produced & 4095) == 0)
                cancellation.ThrowIfCancellationRequested();
            if (FileProduced >= expected)
                throw new RarException(RarFailure.Limit, "RAR LZ output exceeds the declared size.");
            if (Produced - flushed == dictionary.Length)
                Flush();
            RarException.Require(Produced - flushed < dictionary.Length, "RAR filter exceeds the available dictionary.");
            dictionary[(int)(Produced % dictionary.Length)] = b;
            Produced++;
            if (Produced >= nextFlush)
                Flush();
        }

        /// <summary>
        /// Copies a validated backward match from history, allowing overlap with newly produced bytes.
        /// </summary>
        internal void Match(long distance, int length) {
            RarException.Require(distance > 0 && distance <= dictionary.Length && distance <= Produced && length > 0,
                "Invalid RAR match distance or length.");
            if (length > expected - FileProduced)
                throw new RarException(RarFailure.Limit, "RAR match exceeds the declared size.");
            for (int i = 0; i < length; i++)
                Literal(dictionary[(int)((Produced - distance) % dictionary.Length)]);
        }

        #endregion

        #region Filters and output

        /// <summary>
        /// Queues a bounded filter block, allowing identical chained ranges only when explicitly requested.
        /// </summary>
        internal void Schedule(RarFilter filter, bool allowChain) {
            RarException.Require(filter.Start >= Produced && filter.Length > 0 && filter.Length <= dictionary.Length &&
                filter.Start - fileStart <= expected - filter.Length, "RAR filter lies outside its file.");
            if (filters.Count >= maxFilters)
                throw new RarException(RarFailure.Limit, "Too many pending RAR filters.");
            if (filters.TryPeek(out _)) {
                var last = filters.Last();
                RarException.Require(filter.Start >= last.Start + last.Length ||
                    (allowChain && filter.Start == last.Start && filter.Length == last.Length), "Overlapping RAR filters.");
            }
            filters.Enqueue(filter);
        }

        /// <summary>
        /// Flushes all output and verifies that filters, declared length and entry integrity are complete.
        /// </summary>
        internal void Complete() {
            Flush();
            RarException.Require(filters.Count == 0 && flushed == Produced && FileProduced == expected,
                "Incomplete RAR output or filter region.");
            output.Complete();
        }

        /// <summary>
        /// Emits available raw spans or complete filter blocks, leaving unfinished filter input in the dictionary.
        /// </summary>
        private void Flush() {
            cancellation.ThrowIfCancellationRequested();
            while (flushed < Produced) {
                if (filters.TryPeek(out var filter) && flushed == filter.Start) {
                    if (Produced - flushed < filter.Length) {
                        nextFlush = filter.Start + filter.Length;
                        return;
                    }
                    var data = new byte[filter.Length];
                    for (int i = 0; i < data.Length; i++)
                        data[i] = dictionary[(int)((flushed + i) % dictionary.Length)];
                    do {
                        filters.Dequeue();
                        data = filter.Transform(data, output.Count);
                        RarException.Require(data.Length == filter.Length, "RAR filter changed the declared block length.");
                    }
                    while (filters.TryPeek(out filter) && filter.Start == flushed);
                    output.Write(data);
                    flushed += data.Length;
                    continue;
                }
                long end = filters.TryPeek(out filter) ? Math.Min(Produced, filter.Start) : Produced;
                int offset = (int)(flushed % dictionary.Length);
                int count = (int)Math.Min(Math.Min(end - flushed, dictionary.Length - offset), 65536);
                RarException.Require(count > 0, "Invalid RAR filter order.");
                output.Write(dictionary.AsSpan(offset, count));
                flushed += count;
            }
            nextFlush = Produced + 65536;
        }

        #endregion
    }

}