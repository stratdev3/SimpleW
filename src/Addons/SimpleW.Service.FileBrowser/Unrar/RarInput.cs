using System.Globalization;
using System.Text.RegularExpressions;

namespace SimpleW.Service.FileBrowser.Unrar {

    /// <summary>
    /// Identifies a bounded byte range in a caller-owned, seekable source stream.
    /// </summary>
    /// <param name="Source">Readable, seekable stream whose lifetime is managed by the caller.</param>
    /// <param name="Offset">Starting byte offset in the source stream.</param>
    /// <param name="Length">Number of bytes included in the range.</param>
    internal readonly record struct RarRange(Stream Source, long Offset, long Length);

    // A view only: the caller owns the authorized sources, including their lifetime.
    /// <summary>
    /// Exposes validated source ranges as one read-only, seekable stream without owning their lifetime.
    /// </summary>
    internal sealed class RarInput : Stream {

        #region State

        /// <summary>
        /// Stores the ordered source ranges exposed by this stream view.
        /// </summary>
        private readonly RarRange[] ranges;

        /// <summary>
        /// Stores cumulative range offsets, with a final sentinel equal to the logical stream length.
        /// </summary>
        private readonly long[] starts;

        /// <summary>
        /// Cancels reads from the underlying source streams.
        /// </summary>
        private readonly CancellationToken cancellation;

        /// <summary>
        /// Stores the current byte offset in the concatenated logical stream.
        /// </summary>
        private long position;

        #endregion

        #region Construction

        /// <summary>
        /// Validates readable, seekable source ranges and builds their cumulative offsets without taking
        /// ownership.
        /// </summary>
        internal RarInput(IEnumerable<RarRange> ranges, CancellationToken cancellation) {
            this.ranges = ranges.ToArray();
            this.cancellation = cancellation;
            starts = new long[this.ranges.Length + 1];
            for (int i = 0; i < this.ranges.Length; i++) {
                var r = this.ranges[i];
                if (!r.Source.CanRead || !r.Source.CanSeek)
                    throw new ArgumentException("RAR inputs must be readable and seekable.");
                RarException.Require(r.Offset >= 0 && r.Length >= 0 && r.Offset <= r.Source.Length - r.Length,
                    "RAR data extends beyond an input volume.");
                RarException.Require(starts[i] <= long.MaxValue - r.Length, "RAR input size overflow.");
                starts[i + 1] = starts[i] + r.Length;
            }
        }

        #endregion

        #region Reading and seeking

        /// <summary>
        /// Reads from the range containing the current logical position and rejects unexpectedly truncated source
        /// data.
        /// </summary>
        public override int Read(Span<byte> buffer) {
            cancellation.ThrowIfCancellationRequested();
            if (buffer.IsEmpty || position == Length)
                return 0;
            int lo = 0, hi = ranges.Length;
            while (lo < hi) {
                int mid = lo + (hi - lo) / 2;
                if (starts[mid + 1] <= position)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            var range = ranges[lo];
            long offset = position - starts[lo];
            range.Source.Position = range.Offset + offset;
            int count = (int)Math.Min(buffer.Length, range.Length - offset);
            int read = range.Source.Read(buffer[..count]);
            if (read == 0)
                throw new RarException(RarFailure.Incomplete, "RAR source changed or was truncated.");
            position += read;
            return read;
        }

        /// <summary>
        /// Reads from the range containing the current logical position and rejects unexpectedly truncated source
        /// data.
        /// </summary>
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        /// <summary>
        /// Reads one byte from the logical stream, returning minus one at its end.
        /// </summary>
        public override int ReadByte() {
            Span<byte> one = stackalloc byte[1];
            return Read(one) == 0 ? -1 : one[0];
        }

        /// <summary>
        /// Moves within the logical stream, rejecting positions outside its declared range.
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin) {
            long start = origin switch {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => position,
                SeekOrigin.End => Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            long value = checked(start + offset);
            if (value < 0 || value > Length)
                throw new IOException("Seek outside RAR input.");
            return position = value;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether this stream view supports reading; always true.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// Gets whether this stream view supports seeking; always true.
        /// </summary>
        public override bool CanSeek => true;

        /// <summary>
        /// Gets whether this stream view supports writing; always false.
        /// </summary>
        public override bool CanWrite => false;

        /// <summary>
        /// Gets the combined length of all source ranges in bytes.
        /// </summary>
        public override long Length => starts[^1];

        /// <summary>
        /// Gets or sets the byte position within the concatenated source ranges.
        /// </summary>
        public override long Position { get => position; set => Seek(value, SeekOrigin.Begin); }

        #endregion

        #region Read-only stream contract

        /// <summary>
        /// Performs no work because the view does not buffer writes.
        /// </summary>
        public override void Flush() { }

        /// <summary>
        /// Rejects resizing this read-only stream view.
        /// </summary>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>
        /// Rejects writing to this read-only stream view.
        /// </summary>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    /// <summary>
    /// Resolves supported volume naming conventions during planning, before the host authorizes each path.
    /// </summary>
    internal static class RarVolumes {

        #region Volume discovery

        // Discovery is a separate planning step. No decoder opens paths or discovers more volumes.
        // The host must authorize and snapshot EVERY returned path before opening the streams.
        /// <summary>
        /// Resolves contiguous partNN.rar, rar/r00 or numeric volumes from the first part in one directory.
        /// </summary>
        /// <remarks>The caller must authorize and snapshot every returned path before opening any extraction streams.</remarks>
        internal static IReadOnlyList<string> Resolve(string firstPath, int maxVolumes = 10_000, CancellationToken cancellation = default) {
            if (maxVolumes < 1)
                throw new ArgumentOutOfRangeException(nameof(maxVolumes));
            string full = Path.GetFullPath(firstPath);
            string directory = Path.GetDirectoryName(full)!;
            string name = Path.GetFileName(full);
            var part = Regex.Match(name, @"^(.*)\.part(\d+)\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var numeric = Regex.Match(name, @"^(.*)\.(\d{3})$", RegexOptions.CultureInvariant);
            var old = Regex.Match(name, @"^.*\.[r-z]\d{2}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (old.Success || (part.Success && !IsOne(part.Groups[2].Value)) ||
                (numeric.Success && numeric.Groups[2].Value != "001"))
                throw new RarException(RarFailure.SecondaryVolume, "Extraction must start from the first RAR volume.");
            string pattern;
            Func<Match, int> getNumber;
            if (part.Success) {
                pattern = "^" + Regex.Escape(part.Groups[1].Value) + @"\.part(\d+)\.rar$";
                getNumber = m => Number(m.Groups[1].Value);
            }
            else if (numeric.Success) {
                pattern = "^" + Regex.Escape(numeric.Groups[1].Value) + @"\.(\d{3})$";
                getNumber = m => Number(m.Groups[1].Value);
            }
            else if (name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)) {
                pattern = "^" + Regex.Escape(name[..^4]) + @"\.(rar|[r-z]\d{2})$";
                getNumber = m => m.Groups[1].Value.Equals("rar", StringComparison.OrdinalIgnoreCase) ? 1 :
                    2 + (char.ToLowerInvariant(m.Groups[1].Value[0]) - 'r') * 100 + Number(m.Groups[1].Value[1..]);
            }
            else
                throw RarException.Unsupported("Expected .rar, .part01.rar or .001.");
            var matcher = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var paths = new SortedDictionary<int, string>();
            foreach (string path in Directory.EnumerateFiles(directory)) {
                cancellation.ThrowIfCancellationRequested();
                var match = matcher.Match(Path.GetFileName(path));
                if (!match.Success)
                    continue;
                int number = getNumber(match);
                RarException.Require(number >= 1 && paths.TryAdd(number, path), "Ambiguous RAR volume numbering.");
                if (paths.Count > maxVolumes)
                    throw new RarException(RarFailure.Limit, "Too many RAR volumes.");
            }
            if (!paths.TryGetValue(1, out var first) || !string.Equals(first, full, StringComparison.OrdinalIgnoreCase))
                throw new RarException(RarFailure.Incomplete, "First RAR volume is missing.");
            int expected = 1;
            foreach (int number in paths.Keys)
                if (number != expected++)
                    throw new RarException(RarFailure.Incomplete, "Missing RAR volume in numbered sequence.");
            return paths.Values.ToArray();
        }

        #endregion

        #region Volume number parsing

        /// <summary>
        /// Checks whether an invariant decimal volume number identifies the first numbered part.
        /// </summary>
        private static bool IsOne(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n == 1;

        /// <summary>
        /// Parses an invariant decimal volume index or rejects an invalid or overflowing value.
        /// </summary>
        private static int Number(string value) {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int n))
                throw RarException.Corrupt("Invalid RAR volume number.");
            return n;
        }

        #endregion
    }

}