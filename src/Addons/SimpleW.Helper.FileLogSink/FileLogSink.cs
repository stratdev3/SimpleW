using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;
using SimpleW.Observability;


namespace SimpleW.Helper.FileLogSink {

    /// <summary>
    /// UTC calendar interval used to roll log files.
    /// </summary>
    public enum FileLogRollingInterval {
        /// <summary>
        /// No calendar rotation.
        /// </summary>
        None,
        /// <summary>
        /// Roll at the first entry processed in a new UTC hour.
        /// </summary>
        Hour,
        /// <summary>
        /// Roll at the first entry processed in a new UTC day.
        /// </summary>
        Day
    }

    /// <summary>
    /// File sink settings, validated and copied when the sink is constructed.
    /// </summary>
    public sealed class FileLogSinkOptions {

        /// <summary>
        /// Required base path, for example logs/server.log. Parent directories are created.
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// UTC calendar rotation, independently combined with the size limit.
        /// </summary>
        public FileLogRollingInterval RollingInterval { get; set; } = FileLogRollingInterval.Day;

        /// <summary>
        /// Approximate file size limit, counting one byte per formatted character. Null disables size rotation.
        /// Non-ASCII text can exceed this estimate. An entry is never split between files.
        /// </summary>
        public long? MaxFileSizeBytes { get; set; } = 100L * 1024 * 1024;

        /// <summary>
        /// Maximum matching files to retain, including the active file. Null disables deletion.
        /// </summary>
        public int? RetainedFileCountLimit { get; set; }

        /// <summary>
        /// Queue capacity in events
        /// </summary>
        public int QueueCapacity { get; set; } = 8192;

        /// <summary>
        /// Memory budget in bytes for the StreamWriter character buffer.
        /// Converted to a character count by dividing by two, with a minimum of 128 characters.
        /// </summary>
        public int BufferSizeBytes { get; set; } = 64 * 1024;

        /// <summary>
        /// Periodic flush interval
        /// </summary>
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    }

    /// <summary>
    /// Buffered UTF-8 file sink with a bounded queue and one background writer.
    /// Register Write using Log.AddSink or Log.SetSink; remove it and stop producers before disposal.
    /// Use only one sink/process for a given base path. Abrupt termination can lose buffered events.
    /// </summary>
    public sealed class FileLogSink : IDisposable, IAsyncDisposable {

        #region options

        /// <summary>
        /// Directory containing logs
        /// </summary>
        private readonly string _directory;

        /// <summary>
        /// File prefix
        /// </summary>
        private readonly string _prefix;

        /// <summary>
        /// File extension
        /// </summary>
        private readonly string _extension;

        /// <summary>
        /// UTC calendar rotation, independently combined with the size limit.
        /// </summary>
        private readonly FileLogRollingInterval _rollingInterval;

        /// <summary>
        /// Approximate file size limit, counting one byte per formatted character. Null disables size rotation.
        /// Non-ASCII text can exceed this estimate. An entry is never split between files.
        /// </summary>
        private readonly long? _maxFileSizeBytes;

        /// <summary>
        /// Maximum matching files to retain, including the active file. Null disables deletion.
        /// </summary>
        private readonly int? _retainedFileCountLimit;

        /// <summary>
        /// Memory budget in bytes for the StreamWriter character buffer.
        /// Converted to a character count by dividing by two, with a minimum of 128 characters.
        /// </summary>
        private readonly int _bufferSizeBytes;

        /// <summary>
        /// Periodic flush interval
        /// </summary>
        private readonly TimeSpan _flushInterval;

        #endregion options

        /// <summary>
        /// LogEntry Queue
        /// </summary>
        private readonly Channel<PendingWrite> _queue;

        /// <summary>
        /// Main Worker
        /// </summary>
        private readonly Task _worker;

        /// <summary>
        /// Error
        /// </summary>
        private ExceptionDispatchInfo? _error;

        /// <summary>
        /// Closed
        /// </summary>
        private int _closed;

        /// <summary>
        /// Constructor
        /// Validate and copy settings, create the directory, open the file and apply retention synchronously.
        /// Start the background writer only after initialization succeeds. Startup failures are thrown here.
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public FileLogSink(FileLogSinkOptions options) {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentException.ThrowIfNullOrWhiteSpace(options.FilePath);

            if (!Enum.IsDefined(options.RollingInterval)) {
                throw new ArgumentOutOfRangeException(nameof(options.RollingInterval));
            }
            if (options.MaxFileSizeBytes is <= 0) {
                throw new ArgumentOutOfRangeException(nameof(options.MaxFileSizeBytes));
            }
            if (options.RetainedFileCountLimit is <= 0) {
                throw new ArgumentOutOfRangeException(nameof(options.RetainedFileCountLimit));
            }
            if (options.QueueCapacity <= 0) {
                throw new ArgumentOutOfRangeException(nameof(options.QueueCapacity));
            }
            if (options.BufferSizeBytes <= 0) {
                throw new ArgumentOutOfRangeException(nameof(options.BufferSizeBytes));
            }
            if (options.FlushInterval < TimeSpan.FromMilliseconds(1)
                || options.FlushInterval > TimeSpan.FromMilliseconds(uint.MaxValue - 1)
            ) {
                throw new ArgumentOutOfRangeException(nameof(options.FlushInterval));
            }

            string fullPath = Path.GetFullPath(options.FilePath);
            string fileName = Path.GetFileName(fullPath);
            string stem = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(stem) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
                throw new ArgumentException("FilePath must contain a valid file name.", nameof(options.FilePath));
            }

            _directory = Path.GetDirectoryName(fullPath)!;
            _prefix = stem + "-";
            _extension = Path.GetExtension(fullPath);
            _rollingInterval = options.RollingInterval;
            _maxFileSizeBytes = options.MaxFileSizeBytes;
            _retainedFileCountLimit = options.RetainedFileCountLimit;
            _bufferSizeBytes = options.BufferSizeBytes;
            _flushInterval = options.FlushInterval;

            _queue = Channel.CreateBounded<PendingWrite>(new BoundedChannelOptions(options.QueueCapacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            try {
                Directory.CreateDirectory(_directory);
                OpenLogFile(CurrentPeriod());
                _worker = Task.Run(RunAsync);
            }
            catch {
                try {
                    _fileWriter?.Dispose();
                }
                catch {
                    // preserve the initialization error if closing the partially initialized writer also fails.
                }
                _fileWriter = null;
                throw;
            }
        }

        /// <summary>
        /// The first background failure, or null. Failures are never logged through Log.
        /// </summary>
        public Exception? LastError => Volatile.Read(ref _error)?.SourceException;

        /// <summary>
        /// Enqueue an entry without formatting or disk I/O on the caller. Waits if the queue is full.
        /// Throws after closure or a background failure. Queue order is the order of acceptance.
        /// </summary>
        /// <param name="entry"></param>
        public void Write(LogEntry entry) {
            ThrowIfUnavailable();

            PendingWrite pending = new(entry);

            // fast path
            if (_queue.Writer.TryWrite(pending)) {
                return;
            }

            // wait for a free slot in the queue
            try {
                _queue.Writer.WriteAsync(pending).AsTask().GetAwaiter().GetResult();
            }
            catch (ChannelClosedException) {
                ThrowIfUnavailable();
                throw;
            }
        }

        /// <summary>
        /// Wait for all previously accepted entries and flush the file buffers (not an fsync).
        /// Cancellation stops waiting; it does not discard accepted entries or an enqueued flush.
        /// </summary>
        /// <param name="cancellationToken"></param>
        public async Task FlushAsync(CancellationToken cancellationToken = default) {
            ThrowIfUnavailable();
            cancellationToken.ThrowIfCancellationRequested();

            TaskCompletionSource<ExceptionDispatchInfo?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            try {
                await _queue.Writer.WriteAsync(new PendingWrite(default, completion), cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException) {
                ThrowIfUnavailable();
                throw;
            }

            // Carry the failure as a result so cancelling the waiter cannot leave an
            // unobserved faulted flush task behind if the writer subsequently fails.
            ExceptionDispatchInfo? error = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            error?.Throw();
        }

        #region writer

        private StringBuilder _line = new(512);
        private long _estimatedFileBytes;
        private StreamWriter? _fileWriter;
        private string? _activePath;
        private long _period = -1;
        private long _sequence;

        /// <summary>
        /// Pending Write
        /// </summary>
        /// <param name="Entry"></param>
        /// <param name="Flush"></param>
        private readonly record struct PendingWrite(LogEntry Entry, TaskCompletionSource<ExceptionDispatchInfo?>? Flush = null);

        /// <summary>
        /// Log File
        /// </summary>
        /// <param name="Path"></param>
        /// <param name="Period"></param>
        /// <param name="Sequence"></param>
        private readonly record struct LogFile(string Path, long Period, long Sequence);

        /// <summary>
        /// Consume queued entries and flush requests as the only background file writer.
        /// Flush periodically, drain accepted entries on normal closure, and release resources on exit.
        /// </summary>
        /// <returns>A task that completes after cleanup, or reports the first background failure.</returns>
        private async Task RunAsync() {
            PeriodicTimer? timer = null;
            TaskCompletionSource<ExceptionDispatchInfo?>? activeFlush = null;

            try {
                timer = new PeriodicTimer(_flushInterval);
                Task<bool> tick = timer.WaitForNextTickAsync().AsTask();
                Task<bool>? readable = null;

                while (true) {
                    if (tick.IsCompleted) {
                        await tick.ConfigureAwait(false);
                        await FlushFileAsync().ConfigureAwait(false);
                        tick = timer.WaitForNextTickAsync().AsTask();
                    }

                    int processed = 0;
                    while (processed < 256 && _queue.Reader.TryRead(out PendingWrite pending)) {
                        activeFlush = pending.Flush;
                        if (activeFlush != null) {
                            await FlushFileAsync().ConfigureAwait(false);
                            activeFlush.TrySetResult(null);
                            activeFlush = null;
                        }
                        else {
                            await WriteEntryAsync(pending.Entry).ConfigureAwait(false);
                        }
                        pending = default;
                        processed++;
                    }
                    if (processed == 256) {
                        // Check the timer even under continuous load.
                        continue;
                    }

                    readable ??= _queue.Reader.WaitToReadAsync().AsTask();
                    if (await Task.WhenAny(readable, tick).ConfigureAwait(false) == readable) {
                        bool hasEntries = await readable.ConfigureAwait(false);
                        readable = null;
                        if (!hasEntries) {
                            break;
                        }
                    }
                }
                await FlushFileAsync().ConfigureAwait(false);
            }
            catch (Exception ex) {
                Fail(ex);
                activeFlush?.TrySetResult(Volatile.Read(ref _error));
            }
            finally {
                timer?.Dispose();
                try {
                    if (_fileWriter != null) {
                        await _fileWriter.DisposeAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex) {
                    Fail(ex);
                }
                finally {
                    _fileWriter = null;
                    _line.Clear();
                }
            }

            Volatile.Read(ref _error)?.Throw();
        }

        /// <summary>
        /// Format an entry and rotate the file when the UTC period changes or the estimated size requires it.
        /// Count one byte per formatted character and let StreamWriter encode and buffer the complete entry.
        /// </summary>
        /// <param name="entry">The log entry taken from the queue.</param>
        private async Task WriteEntryAsync(LogEntry entry) {
            string text = FormatEntry(entry);
            int estimatedBytes = text.Length;
            long period = CurrentPeriod();
            bool exceedsSize = _maxFileSizeBytes is long limit && _estimatedFileBytes > 0
                               && (_estimatedFileBytes >= limit || estimatedBytes > limit - _estimatedFileBytes);

            if (period != _period || exceedsSize) {
                await _fileWriter!.DisposeAsync().ConfigureAwait(false);
                _fileWriter = null;
                OpenLogFile(period);
            }

            await _fileWriter!.WriteAsync(text).ConfigureAwait(false);
            _estimatedFileBytes = checked(_estimatedFileBytes + estimatedBytes);
        }

        /// <summary>
        /// Flush the StreamWriter text and encoding buffers, then its underlying file stream.
        /// Does not force a durable write to the storage device.
        /// </summary>
        private Task FlushFileAsync() => _fileWriter!.FlushAsync();

        /// <summary>
        /// Rethrow the first background failure, or throw ObjectDisposedException if the sink is closed.
        /// </summary>
        private void ThrowIfUnavailable() {
            Volatile.Read(ref _error)?.Throw();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
        }

        /// <summary>
        /// Record the first background failure and close the queue to release waiting producers.
        /// Discard remaining queued entries and complete queued flush requests with the recorded failure.
        /// </summary>
        /// <param name="error">The background exception; an earlier recorded failure takes precedence.</param>
        private void Fail(Exception error) {
            Interlocked.CompareExchange(ref _error, ExceptionDispatchInfo.Capture(error), null);

            // Complete without a channel error: callers rethrow the original failure above.
            // This also releases saturated producers and any pending WaitToReadAsync.
            _queue.Writer.TryComplete();
            while (_queue.Reader.TryRead(out PendingWrite pending)) {
                pending.Flush?.TrySetResult(Volatile.Read(ref _error));
            }
        }

        /// <summary>
        /// Build the complete log text with CRLF line endings for the StreamWriter.
        /// Include the timestamp, level, source, optional trace/span IDs, message and exception.
        /// </summary>
        /// <param name="entry">The log entry to format.</param>
        /// <returns>The formatted text, including the final CRLF.</returns>
        private string FormatEntry(LogEntry entry) {
            _line.Clear();
            Span<char> timestamp = stackalloc char[33];

            // timestamp
            entry.TimestampUtc.TryFormat(timestamp, out int written, "O", CultureInfo.InvariantCulture);
            
            // level
            _line.Append(timestamp[..written]).Append(" [").Append(entry.LevelName3Len).Append("] ");
            AppendNormalized(entry.Source);
            if (entry.HasActivity) {
                if (entry.TraceId != default) {
                    _line.Append(" [trace=").Append(entry.TraceIdString).Append(']');
                }
                if (entry.SpanId != default) {
                    _line.Append(" [span=").Append(entry.SpanIdString).Append(']');
                }
            }
            _line.Append(' ');

            // message
            AppendNormalized(entry.Message);

            // line end
            _line.Append("\r\n");
            if (entry.Exception != null) {
                AppendNormalized(entry.Exception.ToString());
                _line.Append("\r\n");
            }

            string text = _line.ToString();
            // do not keep an exceptional multi-megabyte StringBuilder alive indefinitely
            if (_line.Capacity > 64 * 1024) {
                _line = new StringBuilder(512);
            }
            return text;
        }

        /// <summary>
        /// Append text to the line builder, converting CR, LF and CRLF line endings to CRLF.
        /// </summary>
        /// <param name="text">The text to append; null and empty strings add nothing.</param>
        private void AppendNormalized(string? text) {
            ReadOnlySpan<char> remaining = text.AsSpan();
            int newline;

            while ((newline = remaining.IndexOfAny('\r', '\n')) >= 0) {
                _line.Append(remaining[..newline]).Append("\r\n");
                int consumed = newline + 1;
                if (remaining[newline] == '\r' && consumed < remaining.Length && remaining[consumed] == '\n') {
                    consumed++;
                }
                remaining = remaining[consumed..];
            }

            _line.Append(remaining);
        }

        /// <summary>
        /// Calculate the rotation period from the current UTC clock, independently of entry timestamps.
        /// </summary>
        /// <returns>The number of hours or days since DateTime.MinValue, or zero when calendar rotation is disabled.</returns>
        private long CurrentPeriod() => _rollingInterval switch {
            FileLogRollingInterval.Hour => DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour,
            FileLogRollingInterval.Day => DateTime.UtcNow.Ticks / TimeSpan.TicksPerDay,
            _ => 0
        };

        /// <summary>
        /// Create a new active log file using the next sequence number, without overwriting existing files.
        /// Scan matching files when entering a new period, reset the size estimate, and apply retention.
        /// </summary>
        /// <param name="period">The UTC rotation period, or zero when calendar rotation is disabled.</param>
        private void OpenLogFile(long period) {
            if (period != _period) {
                _sequence = 0;
                foreach (LogFile segment in FindLogFiles()) {
                    if (segment.Period == period) {
                        _sequence = Math.Max(_sequence, segment.Sequence);
                    }
                }
                _period = period;
            }

            // Size rotations reuse the sequence without rescanning the directory.
            // CreateNew prevents overwriting a segment even if an external file appears.
            string suffix = _rollingInterval switch {
                FileLogRollingInterval.Hour => new DateTime(period * TimeSpan.TicksPerHour, DateTimeKind.Utc).ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + "-",
                FileLogRollingInterval.Day => new DateTime(period * TimeSpan.TicksPerDay, DateTimeKind.Utc).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-",
                _ => string.Empty
            };

            _sequence = checked(_sequence + 1);
            string path = Path.Combine(_directory, _prefix + suffix + _sequence.ToString("D6", CultureInfo.InvariantCulture) + _extension);
            FileStream stream = new(path, new FileStreamOptions {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                BufferSize = 1,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            try {
                _fileWriter = new StreamWriter(stream, new UTF8Encoding(false), Math.Max(128, _bufferSizeBytes / sizeof(char)));
            }
            catch {
                stream.Dispose();
                throw;
            }
            _activePath = path;
            _estimatedFileBytes = 0;
            ApplyRetention();
        }

        /// <summary>
        /// Enumerate files whose names match the configured prefix, extension and rotation scheme.
        /// </summary>
        /// <returns>The matching file paths, periods and sequence numbers, without sorting.</returns>
        private IEnumerable<LogFile> FindLogFiles() {
            foreach (string path in Directory.EnumerateFiles(_directory)) {
                if (TryParseLogFile(path, out LogFile segment)) {
                    yield return segment;
                }
            }
        }

        /// <summary>
        /// Check that a file name matches the configured naming scheme and extract its period and sequence.
        /// Inspect only the name; the file contents are not read.
        /// </summary>
        /// <param name="path">The file path whose name is inspected.</param>
        /// <param name="logfile">The parsed file description on success, or the default value on failure.</param>
        /// <returns>True when the name, optional UTC date and positive sequence number are valid.</returns>
        private bool TryParseLogFile(string path, out LogFile logfile) {
            logfile = default;
            string name = Path.GetFileName(path);
            if (!name.StartsWith(_prefix, StringComparison.Ordinal) || !name.EndsWith(_extension, StringComparison.Ordinal)
                || name.Length < _prefix.Length + _extension.Length + 6) {
                return false;
            }
            ReadOnlySpan<char> suffix = name.AsSpan(_prefix.Length, name.Length - _prefix.Length - _extension.Length);
            int dateLength = _rollingInterval == FileLogRollingInterval.Hour ? 10 : _rollingInterval == FileLogRollingInterval.Day ? 8 : 0;
            long period = 0;
            if (dateLength > 0) {
                if (suffix.Length < dateLength + 7 || suffix[dateLength] != '-') {
                    return false;
                }
                ReadOnlySpan<char> date = suffix[..dateLength];
                string format = dateLength == 10 ? "yyyyMMddHH" : "yyyyMMdd";
                if (!DateTime.TryParseExact(date, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate)) {
                    return false;
                }
                period = parsedDate.Ticks / (dateLength == 10 ? TimeSpan.TicksPerHour : TimeSpan.TicksPerDay);
                suffix = suffix[(dateLength + 1)..];
            }
            // D6 has exactly six digits until the counter grows beyond 999999.
            if (suffix.Length < 6 || (suffix.Length > 6 && suffix[0] == '0')
                || !long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out long sequence) || sequence <= 0) {
                return false;
            }
            logfile = new LogFile(path, period, sequence);
            return true;
        }

        /// <summary>
        /// Delete the oldest matching log files by period and sequence until the configured count is met.
        /// Count the active file toward the limit but never delete it. Do nothing when retention is disabled.
        /// </summary>
        private void ApplyRetention() {
            if (_retainedFileCountLimit is not int limit) {
                return;
            }
            List<LogFile> segments = FindLogFiles().ToList();
            int remove = segments.Count - limit;
            if (remove <= 0) {
                return;
            }

            segments.Sort(static (left, right) => {
                int period = left.Period.CompareTo(right.Period);
                return period != 0 ? period : left.Sequence.CompareTo(right.Sequence);
            });

            foreach (LogFile segment in segments) {
                if (string.Equals(segment.Path, _activePath, StringComparison.Ordinal)) {
                    continue;
                }
                File.Delete(segment.Path);
                if (--remove == 0) {
                    break;
                }
            }

        }

        /// <summary>
        /// Mark the sink as closed and stop the queue from accepting new entries.
        /// Leave accepted entries for the worker to drain; repeated calls have no effect.
        /// </summary>
        private void Complete() {
            if (Interlocked.Exchange(ref _closed, 1) == 0) {
                _queue.Writer.TryComplete();
            }
        }

        #endregion writer

        /// <summary>
        /// Stop admissions, drain accepted entries, flush and close. May block or throw on failure.
        /// </summary>
        public void Dispose() {
            Complete();
            _worker.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Stop admissions, drain accepted entries, flush and close. Repeated calls await the same worker.
        /// </summary>
        public ValueTask DisposeAsync() {
            Complete();
            return new ValueTask(_worker);
        }

    }

}
