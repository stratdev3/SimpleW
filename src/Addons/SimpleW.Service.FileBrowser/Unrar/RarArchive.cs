namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Combines the metadata and packed ranges of one logical entry across archive volumes.
    /// </summary>
    internal sealed class RarEntry {

        #region State

        /// <summary>
        /// Stores successive packed parts in their validated volume order.
        /// </summary>
        private readonly List<RarPart> parts = new();

        #endregion

        #region Construction

        /// <summary>
        /// Starts a logical entry from its first file header and packed range.
        /// </summary>
        internal RarEntry(RarPart part) => parts.Add(part);

        #endregion

        #region Properties

        /// <summary>
        /// Gets the first part's metadata, which defines the logical entry.
        /// </summary>
        internal RarPart Metadata => parts[0];

        /// <summary>
        /// Gets the final currently known part, including its continuation flag and checksum.
        /// </summary>
        internal RarPart Last => parts[^1];

        /// <summary>
        /// Gets all packed parts in volume order.
        /// </summary>
        internal IReadOnlyList<RarPart> Parts => parts.AsReadOnly();

        /// <summary>
        /// Gets the archived name; destination path validation remains the host's responsibility.
        /// </summary>
        internal string Name => Metadata.Name;

        /// <summary>
        /// Gets the declared unpacked size of the complete logical entry.
        /// </summary>
        internal long Size => Metadata.Size;

        /// <summary>
        /// Gets whether the entry describes a directory rather than file data.
        /// </summary>
        internal bool IsDirectory => Metadata.Directory;

        #endregion

        #region Volume continuation

        /// <summary>
        /// Appends a volume continuation after checking split flags and matching entry metadata.
        /// </summary>
        internal void Continue(RarPart part) {
            var p = Metadata;
            RarException.Require(Last.SplitAfter && part.SplitBefore && !part.Directory && p.Name == part.Name &&
                p.Size == part.Size && p.Generation == part.Generation && p.Method == part.Method &&
                p.DictionarySize == part.DictionarySize && p.Solid == part.Solid, "Inconsistent split RAR file headers.");
            parts.Add(part);
        }

        #endregion
    }

    // Internal stream API, deliberately independent of FileBrowser paths and authorization policy.
    // Callers keep source streams open, and must serialize access to this instance and its streams.
    /// <summary>
    /// Inventories caller-supplied RAR streams and extracts their entries sequentially with integrity checks.
    /// </summary>
    internal sealed class RarArchive {

        #region State

        /// <summary>
        /// Stores logical entries in the order required for sequential extraction.
        /// </summary>
        private readonly List<RarEntry> entries = new();

        /// <summary>
        /// Holds the validated resource limits for this archive instance.
        /// </summary>
        private readonly RarLimits limits;

        /// <summary>
        /// Maintains LZ history across compatible solid entries and manages each entry's output filters.
        /// </summary>
        private readonly RarWindow window = new();

        /// <summary>
        /// Retains coding state for RAR 2.9 through 4.x entries.
        /// </summary>
        private readonly Rar4Decoder legacy;

        /// <summary>
        /// Retains coding state for RAR5 entries.
        /// </summary>
        private readonly Rar5Decoder modern = new();

        /// <summary>
        /// Tracks the next required entry index and the preceding compressed entry's decoder generation.
        /// </summary>
        private int nextIndex, previousGeneration;

        /// <summary>
        /// Prevents reuse after a decoding failure or cancellation has invalidated solid state.
        /// </summary>
        private bool failed;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the metadata inventory in the order in which every entry, including directories, must be
        /// extracted.
        /// </summary>
        internal IReadOnlyList<RarEntry> Entries => entries.AsReadOnly();


        #endregion

        #region Construction

        /// <summary>
        /// Inventories supplied streams, validates volume continuity and quotas, and joins split entries without
        /// decompressing them.
        /// </summary>
        /// <param name="sources">Ordered, authorized and snapshotted readable streams; ownership remains with the caller.</param>
        /// <param name="numericFragments">Whether numeric parts may require concatenation as one binary-split container.</param>
        /// <param name="limits">Resource ceilings, or defaults when omitted.</param>
        /// <param name="cancellation">Token checked during metadata inventory.</param>
        /// <remarks>Source streams must remain open and unchanged; this instance is not safe for concurrent extraction.</remarks>
        internal RarArchive(IReadOnlyList<Stream> sources, bool numericFragments = false,
            RarLimits? limits = null, CancellationToken cancellation = default) {
            this.limits = limits ?? new();
            this.limits.Validate();
            legacy = new(this.limits);
            if (sources.Count == 0)
                throw new ArgumentException("No RAR sources supplied.", nameof(sources));
            if (sources.Count > this.limits.MaxVolumes)
                throw new RarException(RarFailure.Limit, "Too many RAR volumes.");
            var streams = new List<Stream>();
            foreach (Stream source in sources) {
                if (!source.CanRead || !source.CanSeek)
                    throw new ArgumentException("RAR sources must be readable and seekable.");
                streams.Add(new RarInput([new(source, 0, source.Length)], cancellation));
            }
            // .001 may mean numbered RAR volumes, or byte-for-byte fragments of a single archive.
            // Only the latter needs a concatenated container stream (not just packed file ranges).
            if (numericFragments && streams.Count > 1 && RarHeaders.Signature(streams[1]) == 0)
                streams = [new RarInput(streams.Select(s => new RarRange(s, 0, s.Length)), cancellation)];
            long total = 0;
            RarVolumeInfo? previous = null;
            for (int index = 0; index < streams.Count; index++) {
                cancellation.ThrowIfCancellationRequested();
                RarVolumeInfo info = RarHeaders.Read(streams[index], this.limits, cancellation);
                if (index == 0) {
                    if (info.Number > 0 || info.Parts.FirstOrDefault()?.SplitBefore == true ||
                        (info.MultiVolume && !info.First && info.Parts.Any(p => p.Generation >= 29)))
                        throw new RarException(RarFailure.SecondaryVolume, "Extraction must start from the first RAR volume.");
                }
                else {
                    RarException.Require(info.MultiVolume && previous!.MultiVolume && info.Format == previous.Format &&
                        info.Solid == previous.Solid && !info.First, "Inconsistent RAR volume series.");
                    RarException.Require(previous.Next || (!previous.HasEnd && entries.LastOrDefault()?.Last.SplitAfter == true),
                        "Unexpected extra RAR volume.");
                }
                if (info.Number.HasValue)
                    RarException.Require(info.Number.Value == index, "RAR volume number mismatch.");
                for (int partIndex = 0; partIndex < info.Parts.Count; partIndex++) {
                    var part = info.Parts[partIndex];
                    RarException.Require((!part.SplitBefore || (info.MultiVolume && index > 0 && partIndex == 0)) &&
                        (!part.SplitAfter || (info.MultiVolume && partIndex == info.Parts.Count - 1)),
                        "RAR split flags do not coincide with volume boundaries.");
                    var pending = entries.LastOrDefault();
                    if (part.SplitBefore) {
                        if (pending == null)
                            throw new RarException(RarFailure.SecondaryVolume, "RAR file starts in a previous volume.");
                        pending.Continue(part);
                    }
                    else {
                        if (pending?.Last.SplitAfter == true)
                            throw new RarException(RarFailure.Incomplete, "Missing continuation of a RAR file.");
                        if (entries.Count >= this.limits.MaxEntries || part.Size > this.limits.MaxTotalBytes - total)
                            throw new RarException(RarFailure.Limit, "RAR extraction quota exceeded.");
                        total += part.Size;
                        entries.Add(new(part));
                    }
                }
                previous = info;
            }
            if (previous!.Next || entries.LastOrDefault()?.Last.SplitAfter == true)
                throw new RarException(RarFailure.Incomplete, "A required RAR volume is missing.");
            // Fail unsupported generations and memory requests during planning, before output exists.
            bool predecessor = false;
            foreach (var entry in entries) {
                var p = entry.Metadata;
                if (p.Directory)
                    continue;
                RarException.Require(!p.Solid || predecessor, "First RAR file cannot continue a solid stream.");
                predecessor = true;
                if (p.Method == 0)
                    continue;
                if (p.Generation is not (>= 29 and <= 40) and not 50)
                    throw RarException.Unsupported("RAR compression before version 2.9 is not supported by Unrar.");
                if (p.DictionarySize > this.limits.MaxDictionaryBytes)
                    throw new RarException(RarFailure.Limit, "RAR dictionary exceeds the memory limit.");
            }
        }


        #endregion

        #region Sequential extraction

        // Push extraction avoids hidden worker threads or buffering entire uncompressed files.
        // A failed/cancelled extraction poisons the decoder: solid state cannot be resumed safely.
        /// <summary>
        /// Extracts the next entry and checks its size and integrity; failure or cancellation prevents further
        /// extraction.
        /// </summary>
        /// <param name="index">Next inventory index, including directory entries.</param>
        /// <param name="destination">Caller-owned writable destination; directory entries write no bytes.</param>
        /// <param name="cancellation">Token checked during extraction.</param>
        internal void ExtractTo(int index, Stream destination, CancellationToken cancellation = default) {
            if (failed)
                throw new RarException(RarFailure.InvalidSequence, "RAR decoder cannot continue after a failed extraction.");
            if ((uint)index >= entries.Count || index != nextIndex)
                throw new RarException(RarFailure.InvalidSequence, "RAR entries must be extracted in archive order, including directories.");
            if (!destination.CanWrite)
                throw new ArgumentException("Output must be writable.", nameof(destination));
            try {
                cancellation.ThrowIfCancellationRequested();
                var entry = entries[index];
                var metadata = entry.Metadata with { Crc = entry.Last.Crc, Hash = entry.Last.Hash };
                if (entry.IsDirectory) { nextIndex++; return; }
                foreach (var part in entry.Parts)
                    if (part.SplitAfter)
                        CheckPackedPart(part, cancellation);
                using var input = new RarInput(entry.Parts.Select(p => p.Range), cancellation);
                var output = new RarCheckedOutput(destination, metadata, cancellation);
                if (metadata.Method == 0) {
                    RarException.Require(input.Length == metadata.Size, "Stored RAR size mismatch.");
                    byte[] buffer = new byte[65536];
                    int count;
                    while ((count = input.Read(buffer)) != 0)
                        output.Write(buffer.AsSpan(0, count));
                    output.Complete();
                }
                else {
                    int generation = metadata.Generation == 50 ? 50 : 29;
                    if (metadata.Solid) {
                        RarException.Require(previousGeneration == 0 || previousGeneration == generation,
                            "RAR solid entry has no compatible predecessor.");
                        // Stored predecessors do not seed an LZ dictionary or Huffman tables.
                        if (previousGeneration == 0)
                            metadata = metadata with { Solid = false };
                    }
                    window.Begin(metadata, output, limits, cancellation);
                    if (generation == 50)
                        modern.Decode(input, metadata, window, cancellation);
                    else
                        legacy.Decode(input, metadata, window, cancellation);
                    window.Complete();
                    previousGeneration = generation;
                }
                nextIndex++;
            }
            catch (Exception error) when (error is IndexOutOfRangeException or ArgumentOutOfRangeException or OverflowException or DivideByZeroException) {
                failed = true;
                throw RarException.Corrupt("Invalid RAR decoder state: " + error.Message);
            }
            catch { failed = true; throw; }
        }


        #endregion

        #region Packed-part integrity

        /// <summary>
        /// Checks the available CRC or BLAKE2sp digest of an intermediate part's packed bytes.
        /// </summary>
        private static void CheckPackedPart(RarPart part, CancellationToken cancellation) {
            using var input = new RarInput([part.Range], cancellation);
            uint crc = uint.MaxValue;
            var hash = part.Hash == null ? null : new RarBlake2sp();
            byte[] buffer = new byte[65536];
            int count;
            while ((count = input.Read(buffer)) != 0) {
                crc = RarCrc.Update(crc, buffer.AsSpan(0, count));
                hash?.Update(buffer.AsSpan(0, count));
            }
            if (hash != null)
                RarException.Require(hash.Finish().AsSpan().SequenceEqual(part.Hash), "Split RAR packed-data hash mismatch.");
            else if (part.Crc.HasValue)
                RarException.Require(~crc == part.Crc.Value, "Split RAR packed-data CRC mismatch.");
        }

        #endregion
    }

}