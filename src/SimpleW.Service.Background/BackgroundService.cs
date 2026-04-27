using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Cronos;
using SimpleW.Observability;


namespace SimpleW.Service.Background {

    /// <summary>
    /// Background Service
    /// </summary>
    internal sealed class BackgroundService : IBackgroundService {

        private readonly ILogger _log = new Logger<BackgroundService>();
        private readonly BackgroundOptions _options;
        private readonly IBackgroundJobStore _jobStore;
        private readonly Channel<BackgroundJobWorkItem> _channel;
        private readonly ConcurrentDictionary<Guid, BackgroundJobRecord> _jobs = new();
        private readonly ConcurrentQueue<Guid> _completedJobs = new();
        private readonly object _lifetimeLock = new();
        private readonly object _telemetryLock = new();

        private SimpleWServer? _server;
        private BackgroundTelemetry? _telemetry;
        private CancellationTokenSource? _stopCts;
        private Task[] _workerTasks = Array.Empty<Task>();
        private Task[] _schedulerTasks = Array.Empty<Task>();
        private int _queueLength;
        private int _runningJobs;
        private bool _started;
        private bool _stopping;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public BackgroundService(BackgroundOptions options) {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
            _jobStore = _options.JobStore ?? new MemoryBackgroundJobStore();

            _channel = Channel.CreateBounded<BackgroundJobWorkItem>(new BoundedChannelOptions(_options.Capacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = _options.WorkerCount == 1,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        internal void AttachServer(SimpleWServer server) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            EnsureTelemetry();
        }

        public BackgroundJobHandle Enqueue(string name, Func<BackgroundJobContext, Task> work) {
            if (TryEnqueue(name, work, out BackgroundJobHandle handle)) {
                return handle;
            }

            throw new InvalidOperationException("Background queue is full.");
        }

        public bool TryEnqueue(string name, Func<BackgroundJobContext, Task> work, out BackgroundJobHandle handle) {
            return TryEnqueue(name, source: "handler", work, out handle);
        }

        internal bool TryEnqueue(string name, string? source, Func<BackgroundJobContext, Task> work, out BackgroundJobHandle handle) {
            ArgumentNullException.ThrowIfNull(work);
            BackgroundTelemetry? telemetry = EnsureTelemetry();
            string normalizedSource = NormalizeSource(source);

            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Job name cannot be empty.", nameof(name));
            }

            string normalizedName = name.Trim();
            DateTimeOffset enqueuedAtUtc = DateTimeOffset.UtcNow;
            Guid id = Guid.NewGuid();

            BackgroundJobRecord record = new(id, normalizedName, source, enqueuedAtUtc);
            BackgroundJobWorkItem item = new(record, work);

            _jobs[id] = record;
            SaveJob(record);

            if (!_channel.Writer.TryWrite(item)) {
                _jobs.TryRemove(id, out _);
                RemoveJob(id);
                if (telemetry != null && normalizedSource != "cron") {
                    TagList tags = default;
                    tags.Add("source", normalizedSource);
                    tags.Add("reason", "queue_full");
                    telemetry.RejectedTotal.Add(1, tags);
                }
                handle = default;
                return false;
            }

            Interlocked.Increment(ref _queueLength);
            if (telemetry != null) {
                TagList tags = default;
                tags.Add("source", normalizedSource);
                telemetry.EnqueuedTotal.Add(1, tags);
            }

            handle = new BackgroundJobHandle(id, normalizedName, enqueuedAtUtc);
            return true;
        }

        public BackgroundJobSnapshot? GetJob(Guid id) {
            return _jobStore.TryGet(id, out BackgroundJobSnapshot? job) ? job : null;
        }

        public IReadOnlyCollection<BackgroundJobSnapshot> GetJobs() {
            return _jobStore.GetAll()
                        .OrderBy(static job => job.EnqueuedAtUtc)
                        .ToArray();
        }

        public void Start() {
            EnsureTelemetry();

            lock (_lifetimeLock) {
                if (_started) {
                    return;
                }

                _stopping = false;
                _stopCts = new CancellationTokenSource();
                CancellationToken token = _stopCts.Token;

                _workerTasks = Enumerable.Range(0, _options.WorkerCount)
                                         .Select(i => Task.Run(() => WorkerLoopAsync(i, token), CancellationToken.None))
                                         .ToArray();

                _schedulerTasks = _options.Schedules.Where(static s => s.Options.Enabled)
                                          .Select(s => Task.Run(() => SchedulerLoopAsync(s, token), CancellationToken.None))
                                          .ToArray();

                _started = true;
            }

            _log.Info($"background service started with {_options.WorkerCount} worker(s)");
        }

        public async Task StopAsync() {
            CancellationTokenSource? stopCts;
            Task[] tasks;

            lock (_lifetimeLock) {
                if (!_started || _stopping) {
                    return;
                }

                _stopping = true;
                stopCts = _stopCts;
                tasks = _workerTasks.Concat(_schedulerTasks).ToArray();
            }

            try {
                stopCts?.Cancel();
            }
            catch { }

            try {
                await Task.WhenAll(tasks).WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException ex) {
                _log.Warn("background service shutdown timeout; running jobs may still complete later", ex);
                return;
            }
            catch (OperationCanceledException) {
                // normal
            }
            finally {
                bool stopped = tasks.All(static t => t.IsCompleted);

                if (stopped) {
                    lock (_lifetimeLock) {
                        _started = false;
                        _stopping = false;
                        _workerTasks = Array.Empty<Task>();
                        _schedulerTasks = Array.Empty<Task>();

                        try { _stopCts?.Dispose(); }
                        catch { }
                        _stopCts = null;
                    }

                    _log.Info("background service stopped");
                }
            }
        }

        private async Task WorkerLoopAsync(int workerId, CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    while (await _channel.Reader.WaitToReadAsync(token).ConfigureAwait(false)) {
                        while (_channel.Reader.TryRead(out BackgroundJobWorkItem item)) {
                            Interlocked.Decrement(ref _queueLength);
                            await ExecuteAsync(workerId, item, token).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch (Exception ex) {
                    _log.Error($"background worker {workerId} loop error", ex);
                }
            }
        }

        private async Task ExecuteAsync(int workerId, BackgroundJobWorkItem item, CancellationToken token) {
            long startedTimestamp = Stopwatch.GetTimestamp();
            BackgroundJobRecord job = item.Job;
            BackgroundTelemetry? telemetry = EnsureTelemetry();
            string source = NormalizeSource(job.Source);
            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

            job.MarkRunning(startedAtUtc);
            SaveJob(job);

            Interlocked.Increment(ref _runningJobs);
            if (telemetry != null) {
                TagList startTags = default;
                startTags.Add("source", source);
                telemetry.StartedTotal.Add(1, startTags);
                telemetry.QueueWaitDurationMs.Record((startedAtUtc - job.EnqueuedAtUtc).TotalMilliseconds, startTags);
            }

            string result = "succeeded";
            try {
                BackgroundJobContext context = new(job.Id, job.Name, job.Source, job.EnqueuedAtUtc, token, (percent, message) => {
                    job.ReportProgress(percent, message);
                    SaveJob(job);
                    if (telemetry != null) {
                        TagList tags = default;
                        tags.Add("source", source);
                        telemetry.ProgressTotal.Add(1, tags);
                    }
                });
                await item.Work(context).ConfigureAwait(false);

                if (token.IsCancellationRequested) {
                    job.MarkCanceled(DateTimeOffset.UtcNow);
                    result = "canceled";
                    SaveJob(job);
                }
                else {
                    job.MarkSucceeded(DateTimeOffset.UtcNow);
                    result = "succeeded";
                    SaveJob(job);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) {
                job.MarkCanceled(DateTimeOffset.UtcNow);
                result = "canceled";
                SaveJob(job);
            }
            catch (Exception ex) {
                job.MarkFailed(DateTimeOffset.UtcNow, ex);
                result = "failed";
                SaveJob(job);
                _log.Error($"background job '{job.Name}' failed on worker {workerId}", ex);
            }
            finally {
                Interlocked.Decrement(ref _runningJobs);
                if (telemetry != null) {
                    TagList tags = default;
                    tags.Add("source", source);
                    tags.Add("result", result);
                    telemetry.CompletedTotal.Add(1, tags);
                    telemetry.RunDurationMs.Record(Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds, tags);
                }
                RememberCompletedJob(job.Id);
            }
        }

        private async Task SchedulerLoopAsync(BackgroundOptions.BackgroundCronRegistration schedule, CancellationToken token) {
            CronExpression expression = schedule.GetExpression();
            TimeZoneInfo timeZone = schedule.Options.TimeZone ?? _options.TimeZone;
            int active = 0;

            while (!token.IsCancellationRequested) {
                try {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    DateTimeOffset? next = expression.GetNextOccurrence(now, timeZone);

                    if (next == null) {
                        _log.Warn($"cron schedule '{schedule.Name}' has no next occurrence");
                        return;
                    }

                    TimeSpan delay = next.Value - now;
                    if (delay > TimeSpan.Zero) {
                        await Task.Delay(delay, token).ConfigureAwait(false);
                    }

                    if (!schedule.Options.AllowConcurrentExecutions && Interlocked.CompareExchange(ref active, 1, 0) != 0) {
                        _log.Warn($"cron schedule '{schedule.Name}' skipped because previous run is still queued or running");
                        continue;
                    }

                    bool enqueued = TryEnqueue(
                        schedule.Name,
                        source: "cron",
                        async ctx => {
                            try {
                                await schedule.Work(ctx).ConfigureAwait(false);
                            }
                            finally {
                                if (!schedule.Options.AllowConcurrentExecutions) {
                                    Volatile.Write(ref active, 0);
                                }
                            }
                        },
                        out _
                    );

                    if (!enqueued) {
                        if (!schedule.Options.AllowConcurrentExecutions) {
                            Volatile.Write(ref active, 0);
                        }
                        _log.Warn($"cron schedule '{schedule.Name}' could not enqueue because the background queue is full");
                    }
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch (Exception ex) {
                    if (!schedule.Options.AllowConcurrentExecutions) {
                        Volatile.Write(ref active, 0);
                    }
                    _log.Error($"cron schedule '{schedule.Name}' loop error", ex);

                    try {
                        await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                    }
                    catch {
                        return;
                    }
                }
            }
        }

        private void RememberCompletedJob(Guid id) {
            if (_options.CompletedJobRetention == 0) {
                _jobs.TryRemove(id, out _);
                RemoveJob(id);
                return;
            }

            _completedJobs.Enqueue(id);

            while (_completedJobs.Count > _options.CompletedJobRetention && _completedJobs.TryDequeue(out Guid oldId)) {
                _jobs.TryRemove(oldId, out _);
                RemoveJob(oldId);
            }
        }

        private void SaveJob(BackgroundJobRecord job) {
            try {
                _jobStore.Save(job.ToSnapshot());
            }
            catch (Exception ex) {
                _log.Error($"background job store failed to save job '{job.Id}'", ex);
            }
        }

        private void RemoveJob(Guid id) {
            try {
                _jobStore.Remove(id);
            }
            catch (Exception ex) {
                _log.Error($"background job store failed to remove job '{id}'", ex);
            }
        }

        private BackgroundTelemetry? EnsureTelemetry() {
            if (!_options.EnableTelemetry) {
                return null;
            }

            SimpleWServer? server = _server;
            if (server == null) {
                return null;
            }

            Telemetry? telemetry = server.Telemetry;
            if (telemetry == null || !server.IsTelemetryEnabled) {
                return null;
            }

            BackgroundTelemetry? t = _telemetry;
            if (t != null) {
                return t;
            }

            lock (_telemetryLock) {
                _telemetry ??= new BackgroundTelemetry(telemetry.Meter, this);
                return _telemetry;
            }
        }

        private static string NormalizeSource(string? source) {
            if (string.IsNullOrWhiteSpace(source)) {
                return "unknown";
            }

            string normalized = source.Trim().ToLowerInvariant();
            return normalized is "handler" or "cron" ? normalized : "unknown";
        }

        private readonly record struct BackgroundJobWorkItem(BackgroundJobRecord Job, Func<BackgroundJobContext, Task> Work);

        private sealed class BackgroundTelemetry {

            public readonly Counter<long> EnqueuedTotal;
            public readonly Counter<long> RejectedTotal;
            public readonly Counter<long> StartedTotal;
            public readonly Counter<long> CompletedTotal;
            public readonly Counter<long> ProgressTotal;
            public readonly Histogram<double> QueueWaitDurationMs;
            public readonly Histogram<double> RunDurationMs;

            public BackgroundTelemetry(Meter meter, BackgroundService service) {
                EnqueuedTotal = meter.CreateCounter<long>("simplew.background.job.enqueued.count", unit: "job");
                RejectedTotal = meter.CreateCounter<long>("simplew.background.job.rejected.count", unit: "job");
                StartedTotal = meter.CreateCounter<long>("simplew.background.job.started.count", unit: "job");
                CompletedTotal = meter.CreateCounter<long>("simplew.background.job.completed.count", unit: "job");
                ProgressTotal = meter.CreateCounter<long>("simplew.background.job.progress.count", unit: "report");
                QueueWaitDurationMs = meter.CreateHistogram<double>("simplew.background.job.queue_wait.duration", unit: "ms");
                RunDurationMs = meter.CreateHistogram<double>("simplew.background.job.run.duration", unit: "ms");

                meter.CreateObservableGauge<int>("simplew.background.queue.length", () => Volatile.Read(ref service._queueLength), unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.queue.capacity", () => service._options.Capacity, unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.job.running", () => Volatile.Read(ref service._runningJobs), unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.job.tracked", () => service._jobs.Count, unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.worker.count", () => service._options.WorkerCount, unit: "worker");
                meter.CreateObservableGauge<int>(
                    "simplew.background.cron.schedule.enabled",
                    () => service._options.Schedules.Count(static schedule => schedule.Options.Enabled),
                    unit: "schedule"
                );
            }

        }

    }

    /// <summary>
    /// Background Job Record
    /// </summary>
    internal sealed class BackgroundJobRecord {

        private readonly object _sync = new();

        public Guid Id { get; }
        public string Name { get; }
        public string? Source { get; }
        public DateTimeOffset EnqueuedAtUtc { get; }

        private BackgroundJobStatus _status;
        private DateTimeOffset? _startedAtUtc;
        private DateTimeOffset? _finishedAtUtc;
        private string? _error;
        private double? _progress;
        private string? _progressMessage;
        private DateTimeOffset? _updatedAtUtc;

        public BackgroundJobRecord(Guid id, string name, string? source, DateTimeOffset enqueuedAtUtc) {
            Id = id;
            Name = name;
            Source = source;
            EnqueuedAtUtc = enqueuedAtUtc;
            _status = BackgroundJobStatus.Queued;
            _updatedAtUtc = enqueuedAtUtc;
        }

        public void MarkRunning(DateTimeOffset startedAtUtc) {
            lock (_sync) {
                _status = BackgroundJobStatus.Running;
                _startedAtUtc = startedAtUtc;
                _finishedAtUtc = null;
                _error = null;
                _updatedAtUtc = startedAtUtc;
            }
        }

        public void MarkSucceeded(DateTimeOffset finishedAtUtc) {
            lock (_sync) {
                _status = BackgroundJobStatus.Succeeded;
                _finishedAtUtc = finishedAtUtc;
                _error = null;
                if (_progress.HasValue && _progress.Value < 100d) {
                    _progress = 100d;
                }
                _updatedAtUtc = finishedAtUtc;
            }
        }

        public void MarkCanceled(DateTimeOffset finishedAtUtc) {
            lock (_sync) {
                _status = BackgroundJobStatus.Canceled;
                _finishedAtUtc = finishedAtUtc;
                _error = null;
                _updatedAtUtc = finishedAtUtc;
            }
        }

        public void MarkFailed(DateTimeOffset finishedAtUtc, Exception exception) {
            lock (_sync) {
                _status = BackgroundJobStatus.Failed;
                _finishedAtUtc = finishedAtUtc;
                _error = exception.Message;
                _updatedAtUtc = finishedAtUtc;
            }
        }

        public void ReportProgress(double? percent, string? message) {
            lock (_sync) {
                if (percent.HasValue) {
                    _progress = NormalizePercent(percent.Value);
                }

                if (message != null) {
                    _progressMessage = message;
                }

                _updatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public BackgroundJobSnapshot ToSnapshot() {
            lock (_sync) {
                return new BackgroundJobSnapshot(Id, Name, Source, _status, EnqueuedAtUtc, _startedAtUtc, _finishedAtUtc, _error, _progress, _progressMessage, _updatedAtUtc);
            }
        }

        private static double? NormalizePercent(double percent) {
            if (double.IsNaN(percent) || double.IsInfinity(percent)) {
                return null;
            }

            if (percent < 0d) {
                return 0d;
            }
            if (percent > 100d) {
                return 100d;
            }

            return percent;
        }

    }

}
