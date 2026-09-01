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

        #region dependencies and queues

        /// <summary>
        /// Logger used by the background engine.
        /// </summary>
        private readonly ILogger _log = new Logger<BackgroundService>();

        /// <summary>
        /// Validated module options shared by workers and schedulers.
        /// </summary>
        private readonly BackgroundOptions _options;

        /// <summary>
        /// Snapshot store used for job observation and retention.
        /// </summary>
        private readonly IBackgroundJobStore _jobStore;

        /// <summary>
        /// Bounded channel containing jobs ready to run.
        /// </summary>
        private readonly Channel<BackgroundJobWorkItem> _channel;

        /// <summary>
        /// In-process records required to execute and cancel tracked jobs.
        /// </summary>
        private readonly ConcurrentDictionary<Guid, BackgroundJobRecord> _jobs = new();

        /// <summary>
        /// Completion order used to enforce count-based retention.
        /// </summary>
        private readonly ConcurrentQueue<Guid> _completedJobs = new();

        /// <summary>
        /// Priority queue shared by delayed jobs and retries.
        /// </summary>
        private readonly PriorityQueue<BackgroundJobWorkItem, DateTimeOffset> _timedJobs = new();

        /// <summary>
        /// Wakes the timed scheduler when an earlier job is inserted or removed.
        /// </summary>
        private readonly SemaphoreSlim _timedSignal = new(0, 1);

        #endregion dependencies and queues

        #region synchronization and runtime state

        /// <summary>
        /// Protects every access to the non-thread-safe timed priority queue.
        /// </summary>
        private readonly object _timedLock = new();

        /// <summary>
        /// Protects service start and stop transitions.
        /// </summary>
        private readonly object _lifetimeLock = new();

        /// <summary>
        /// Protects lazy telemetry creation.
        /// </summary>
        private readonly object _telemetryLock = new();

        /// <summary>
        /// Server owning this module, attached before startup.
        /// </summary>
        private SimpleWServer? _server;

        /// <summary>
        /// Lazily created telemetry instruments.
        /// </summary>
        private BackgroundTelemetry? _telemetry;

        /// <summary>
        /// Cancels workers, schedulers, timed waits, and running jobs during shutdown.
        /// </summary>
        private CancellationTokenSource? _stopCts;

        /// <summary>
        /// Currently active worker loops.
        /// </summary>
        private Task[] _workerTasks = [];

        /// <summary>
        /// Currently active cron, interval, and timed scheduler loops.
        /// </summary>
        private Task[] _schedulerTasks = [];

        /// <summary>
        /// Number of work items currently published in the bounded channel.
        /// </summary>
        private int _queueLength;

        /// <summary>
        /// Number of delegates currently executing on workers.
        /// </summary>
        private int _runningJobs;

        /// <summary>
        /// Number of live entries waiting in the timed priority queue.
        /// </summary>
        private int _scheduledJobs;

        /// <summary>
        /// Indicates that worker and scheduler loops have been started.
        /// </summary>
        private bool _started;

        /// <summary>
        /// Prevents new jobs from being accepted while shutdown is in progress.
        /// </summary>
        private bool _stopping;

        #endregion synchronization and runtime state

        #region construction and attachment

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public BackgroundService(BackgroundOptions options) {
            // Normalize configuration once so every subsequent job inherits stable defaults.
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
            _jobStore = _options.JobStore ?? new MemoryBackgroundJobStore();

            // Wait mode enables EnqueueAsync backpressure while TryEnqueue still uses TryWrite.
            _channel = Channel.CreateBounded<BackgroundJobWorkItem>(new BoundedChannelOptions(_options.Capacity) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = _options.WorkerCount == 1,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        }

        /// <summary>
        /// Attaches the owning server and initializes telemetry when enabled.
        /// </summary>
        /// <param name="server"></param>
        internal void AttachServer(SimpleWServer server) {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            EnsureTelemetry();
        }

        #endregion construction and attachment

        #region public enqueue API

        /// <inheritdoc />
        public BackgroundJobHandle Enqueue(string name, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null) {
            // The synchronous API deliberately fails fast instead of waiting for capacity.
            if (TryEnqueue(name, work, out BackgroundJobHandle handle, configure)) {
                return handle;
            }

            throw new InvalidOperationException("Background queue is full.");
        }

        /// <inheritdoc />
        public bool TryEnqueue(string name, Func<BackgroundJobContext, Task> work, out BackgroundJobHandle handle, Action<BackgroundJobOptions>? configure = null) {
            return TryEnqueue(name, source: "handler", work, ResolveJobOptions(configure), onCompleted: null, out handle);
        }

        /// <inheritdoc />
        public async ValueTask<BackgroundJobHandle> EnqueueAsync(string name, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null, CancellationToken cancellationToken = default) {
            ThrowIfStopping();

            // Register before waiting so the job has one stable id throughout admission.
            BackgroundJobRecord job = CreateJob(name, source: "handler", work, ResolveJobOptions(configure), scheduledAtUtc: null, onCompleted: null, out BackgroundJobWorkItem item);
            RegisterJob(job);

            // The caller token controls admission only; the service token aborts admission on shutdown.
            CancellationToken stopToken = GetStopToken();
            using CancellationTokenSource? waitCts = stopToken.CanBeCanceled
                                                        ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopToken)
                                                        : null;
            CancellationToken waitToken = waitCts?.Token ?? cancellationToken;

            try {
                bool queued = await QueueExistingAsync(item, waitToken).ConfigureAwait(false);
                if (!queued) {
                    throw new OperationCanceledException(waitToken);
                }
                return job.ToHandle();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !job.IsTerminal) {
                // A canceled admission must not leave an observable phantom snapshot.
                RemoveUnacceptedJob(job);
                throw;
            }
        }

        /// <inheritdoc />
        public BackgroundJobHandle EnqueueAfter(string name, TimeSpan delay, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null) {
            if (delay < TimeSpan.Zero) {
                throw new ArgumentOutOfRangeException(nameof(delay), "Delay cannot be negative.");
            }

            return EnqueueAt(name, DateTimeOffset.UtcNow.Add(delay), work, configure);
        }

        /// <inheritdoc />
        public BackgroundJobHandle EnqueueAt(string name, DateTimeOffset dueAt, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null) {
            ThrowIfStopping();
            DateTimeOffset dueAtUtc = dueAt.ToUniversalTime();
            BackgroundJobRecord job = CreateJob(name, source: "handler", work, ResolveJobOptions(configure), dueAtUtc, onCompleted: null, out BackgroundJobWorkItem item);
            RegisterJob(job);

            // Past dates use the regular fast enqueue path; a full queue falls back to timed waiting.
            if (dueAtUtc <= DateTimeOffset.UtcNow && TryQueueExisting(item)) {
                return job.ToHandle();
            }

            ScheduleTimed(item, dueAtUtc);
            return job.ToHandle();
        }

        #endregion public enqueue API

        #region public control and observation API

        /// <inheritdoc />
        public bool Cancel(Guid id) {
            if (!_jobs.TryGetValue(id, out BackgroundJobRecord? job)) {
                return false;
            }

            // Running jobs are canceled cooperatively; all other states become terminal immediately.
            bool accepted = job.RequestCancellation(DateTimeOffset.UtcNow, out bool completedNow);
            if (!accepted) {
                return false;
            }

            // Physically remove delayed/retrying jobs so they release captured delegates promptly.
            if (job.TryClearTimed()) {
                Interlocked.Decrement(ref _scheduledJobs);
                RemoveTimedJob(job.Id);
            }

            SaveJob(job);
            if (completedNow) {
                CompleteJob(job, "canceled");
            }
            SignalTimedScheduler();
            return true;
        }

        /// <inheritdoc />
        public BackgroundJobSnapshot? GetJob(Guid id) {
            return _jobStore.TryGet(id, out BackgroundJobSnapshot? job) ? job : null;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<BackgroundJobSnapshot> GetJobs() {
            return _jobStore.GetAll()
                        .OrderBy(static job => job.EnqueuedAtUtc)
                        .ToArray();
        }

        #endregion public control and observation API

        #region service lifetime

        /// <summary>
        /// Starts worker loops, recurring schedules, and the shared timed scheduler.
        /// </summary>
        public void Start() {
            EnsureTelemetry();

            lock (_lifetimeLock) {
                // Start is idempotent for the lifetime of one server run.
                if (_started) {
                    return;
                }

                _stopping = false;
                _stopCts = new CancellationTokenSource();
                CancellationToken token = _stopCts.Token;

                // Workers consume only ready items from the bounded channel.
                _workerTasks = Enumerable.Range(0, _options.WorkerCount)
                                         .Select(i => Task.Run(() => WorkerLoopAsync(i, token), CancellationToken.None))
                                         .ToArray();

                IEnumerable<Task> cronTasks = _options.Schedules.Where(static s => s.Options.Enabled)
                                                                .Select(s => Task.Run(() => CronSchedulerLoopAsync(s, token), CancellationToken.None));
                IEnumerable<Task> intervalTasks = _options.Intervals.Where(static s => s.Options.Enabled)
                                                                    .Select(s => Task.Run(() => IntervalSchedulerLoopAsync(s, token), CancellationToken.None));

                // One shared timed loop serves delayed jobs and retry backoffs.
                _schedulerTasks = cronTasks.Concat(intervalTasks)
                                           .Append(Task.Run(() => TimedSchedulerLoopAsync(token), CancellationToken.None))
                                           .ToArray();

                _started = true;
            }

            SignalTimedScheduler();
            _log.Info($"background service started with {_options.WorkerCount} worker(s)");
        }

        /// <summary>
        /// Stops accepting work, cancels every non-terminal job, and waits for all loops to finish.
        /// </summary>
        /// <returns></returns>
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

            // Stop every delay and running delegate through the shared service token.
            try {
                stopCts?.Cancel();
            }
            catch { }

            // Clear captured timed work before marking all remaining jobs canceled.
            ClearTimedJobs();
            foreach (Guid id in _jobs.Keys) {
                Cancel(id);
            }
            SignalTimedScheduler();

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
                // Keep the stopping flag set after a timeout because work may still be executing.
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

        #endregion service lifetime

        #region job admission and queue publication

        /// <summary>
        /// Creates and immediately publishes an internal job, rolling it back when the channel is full.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="source"></param>
        /// <param name="work"></param>
        /// <param name="jobOptions"></param>
        /// <param name="onCompleted"></param>
        /// <param name="handle"></param>
        /// <returns></returns>
        private bool TryEnqueue(string name, string source, Func<BackgroundJobContext, Task> work, BackgroundJobOptions jobOptions, Action? onCompleted, out BackgroundJobHandle handle) {
            ThrowIfStopping();
            BackgroundJobRecord job = CreateJob(name, source, work, jobOptions, scheduledAtUtc: null, onCompleted, out BackgroundJobWorkItem item);
            RegisterJob(job);

            if (!TryQueueExisting(item)) {
                RemoveUnacceptedJob(job);
                RecordRejected(job.Source);
                handle = default;
                return false;
            }

            handle = job.ToHandle();
            return true;
        }

        /// <summary>
        /// Validates input and creates the mutable record plus its immutable work wrapper.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="source"></param>
        /// <param name="work"></param>
        /// <param name="jobOptions"></param>
        /// <param name="scheduledAtUtc"></param>
        /// <param name="onCompleted"></param>
        /// <param name="item"></param>
        /// <returns></returns>
        private BackgroundJobRecord CreateJob(string name, string source, Func<BackgroundJobContext, Task> work, BackgroundJobOptions jobOptions, DateTimeOffset? scheduledAtUtc, Action? onCompleted, out BackgroundJobWorkItem item) {
            ArgumentNullException.ThrowIfNull(work);
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Job name cannot be empty.", nameof(name));
            }

            DateTimeOffset enqueuedAtUtc = DateTimeOffset.UtcNow;
            BackgroundJobRecord job = new(Guid.NewGuid(), name.Trim(), NormalizeSource(source), enqueuedAtUtc, scheduledAtUtc, jobOptions.Clone().ValidateAndNormalize(), onCompleted);
            item = new BackgroundJobWorkItem(job, work);
            return job;
        }

        /// <summary>
        /// Adds a job to in-process tracking and persists its initial snapshot.
        /// </summary>
        /// <param name="job"></param>
        private void RegisterJob(BackgroundJobRecord job) {
            _jobs[job.Id] = job;
            SaveJob(job);
        }

        /// <summary>
        /// Removes a job that never acquired queue capacity.
        /// </summary>
        /// <param name="job"></param>
        private void RemoveUnacceptedJob(BackgroundJobRecord job) {
            if (_jobs.TryRemove(job.Id, out _)) {
                RemoveJob(job.Id);
                job.Dispose();
            }
        }

        /// <summary>
        /// Attempts to atomically transition an existing job to queued and publish it to the channel.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private bool TryQueueExisting(BackgroundJobWorkItem item) {
            if (item.Job.IsTerminal) {
                return false;
            }

            // The record lock surrounds channel publication so a fast worker cannot race MarkQueued.
            Interlocked.Increment(ref _queueLength);
            if (!item.Job.TryPublishQueued(DateTimeOffset.UtcNow, () => {
                if (!_channel.Writer.TryWrite(item)) {
                    return false;
                }
                SaveJob(item.Job);
                RecordEnqueued(item.Job.Source);
                return true;
            })) {
                Interlocked.Decrement(ref _queueLength);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Waits asynchronously for capacity, then atomically publishes an existing job.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async ValueTask<bool> QueueExistingAsync(BackgroundJobWorkItem item, CancellationToken token) {
            // Manual cancellation must wake a job waiting for channel capacity.
            using CancellationTokenSource queueCts = CancellationTokenSource.CreateLinkedTokenSource(token, item.Job.CancellationToken);

            try {
                while (!item.Job.IsTerminal) {
                    queueCts.Token.ThrowIfCancellationRequested();
                    if (!await _channel.Writer.WaitToWriteAsync(queueCts.Token).ConfigureAwait(false)) {
                        return false;
                    }

                    if (item.Job.IsTerminal) {
                        return false;
                    }

                    // Multiple producers may observe capacity simultaneously, so TryWrite remains authoritative.
                    Interlocked.Increment(ref _queueLength);
                    if (item.Job.TryPublishQueued(DateTimeOffset.UtcNow, () => {
                        if (!_channel.Writer.TryWrite(item)) {
                            return false;
                        }
                        SaveJob(item.Job);
                        RecordEnqueued(item.Job.Source);
                        return true;
                    })) {
                        return true;
                    }
                    Interlocked.Decrement(ref _queueLength);
                }
            }
            catch (OperationCanceledException) when (item.Job.IsTerminal) {
                return false;
            }

            return false;
        }

        #endregion job admission and queue publication

        #region worker execution and retry decisions

        /// <summary>
        /// Continuously drains ready work from the channel for one worker.
        /// </summary>
        /// <param name="workerId"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task WorkerLoopAsync(int workerId, CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    while (await _channel.Reader.WaitToReadAsync(token).ConfigureAwait(false)) {
                        while (_channel.Reader.TryRead(out BackgroundJobWorkItem? item)) {
                            Interlocked.Decrement(ref _queueLength);

                            // Canceled queued items remain in the channel but are discarded without execution.
                            if (item != null && !item.Job.IsTerminal) {
                                await ExecuteAsync(workerId, item, token).ConfigureAwait(false);
                            }
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

        /// <summary>
        /// Executes one attempt and resolves it to success, retry, timeout, failure, or cancellation.
        /// </summary>
        /// <param name="workerId"></param>
        /// <param name="item"></param>
        /// <param name="serviceToken"></param>
        /// <returns></returns>
        private async Task ExecuteAsync(int workerId, BackgroundJobWorkItem item, CancellationToken serviceToken) {
            BackgroundJobRecord job = item.Job;
            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

            // Cancellation may win after dequeue but before the attempt begins.
            if (!job.TryBeginAttempt(startedAtUtc, out int attempt, out DateTimeOffset queuedAtUtc)) {
                return;
            }

            BackgroundTelemetry? telemetry = EnsureTelemetry();
            string source = NormalizeSource(job.Source);
            SaveJob(job);
            Interlocked.Increment(ref _runningJobs);

            if (telemetry != null) {
                TagList tags = default;
                tags.Add("source", source);
                telemetry.AttemptedTotal.Add(1, tags);
                if (attempt == 1) {
                    telemetry.StartedTotal.Add(1, tags);
                }
                telemetry.QueueWaitDurationMs.Record((startedAtUtc - queuedAtUtc).TotalMilliseconds, tags);
            }

            long attemptStartedTimestamp = Stopwatch.GetTimestamp();
            string attemptResult = "succeeded";
            Exception? failure = null;

            // Timeout is per attempt and linked with both service and individual job cancellation.
            using CancellationTokenSource? timeoutCts = job.Options.Timeout.HasValue ? new CancellationTokenSource() : null;
            if (timeoutCts != null) {
                timeoutCts.CancelAfter(job.Options.Timeout!.Value);
            }

            using CancellationTokenSource attemptCts = timeoutCts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(serviceToken, job.CancellationToken, timeoutCts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(serviceToken, job.CancellationToken);

            try {
                // Progress changes are observable immediately through the configured snapshot store.
                BackgroundJobContext context = new(job.Id, job.Name, job.Source, job.EnqueuedAtUtc, attemptCts.Token, (percent, message) => {
                    job.ReportProgress(percent, message);
                    SaveJob(job);
                    if (telemetry != null) {
                        TagList tags = default;
                        tags.Add("source", source);
                        telemetry.ProgressTotal.Add(1, tags);
                    }
                });

                await item.Work(context).ConfigureAwait(false);
            }
            catch (Exception ex) {
                // Classify after the delegate returns so timeout/cancellation has precedence over its exception type.
                failure = ex;
            }

            try {
                // Cancellation always wins and is never retried.
                if (job.IsCancellationRequested || serviceToken.IsCancellationRequested) {
                    attemptResult = "canceled";
                    job.MarkCanceled(DateTimeOffset.UtcNow);
                    SaveJob(job);
                    CompleteJob(job, attemptResult);
                }
                else if (timeoutCts?.IsCancellationRequested == true) {
                    // A timed out delegate has already returned here; an uncooperative delegate still occupies the worker.
                    if (telemetry != null) {
                        TagList timeoutTags = default;
                        timeoutTags.Add("source", source);
                        telemetry.TimedOutTotal.Add(1, timeoutTags);
                    }
                    TimeoutException timeout = new($"Background job '{job.Name}' attempt {attempt} exceeded timeout '{job.Options.Timeout}'.", failure);
                    if (job.Options.RetryOnTimeout && job.CanRetry) {
                        attemptResult = "retry";
                        ScheduleRetry(item, timeout, timedOut: true);
                    }
                    else {
                        attemptResult = "timed_out";
                        job.MarkTimedOut(DateTimeOffset.UtcNow, timeout);
                        SaveJob(job);
                        CompleteJob(job, attemptResult);
                        _log.Error($"background job '{job.Name}' timed out on worker {workerId}", timeout);
                    }
                }
                else if (failure != null) {
                    if (job.CanRetry) {
                        attemptResult = "retry";
                        ScheduleRetry(item, failure, timedOut: false);
                    }
                    else {
                        attemptResult = "failed";
                        job.MarkFailed(DateTimeOffset.UtcNow, failure);
                        SaveJob(job);
                        CompleteJob(job, attemptResult);
                        _log.Error($"background job '{job.Name}' failed on worker {workerId}", failure);
                    }
                }
                else {
                    attemptResult = "succeeded";
                    job.MarkSucceeded(DateTimeOffset.UtcNow);
                    SaveJob(job);
                    CompleteJob(job, attemptResult);
                }
            }
            finally {
                Interlocked.Decrement(ref _runningJobs);
                if (telemetry != null) {
                    TagList tags = default;
                    tags.Add("source", source);
                    tags.Add("result", attemptResult);
                    telemetry.AttemptCompletedTotal.Add(1, tags);
                    telemetry.RunDurationMs.Record(Stopwatch.GetElapsedTime(attemptStartedTimestamp).TotalMilliseconds, tags);
                }
            }
        }

        /// <summary>
        /// Records a failed attempt and schedules the same work item for its next attempt.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="exception"></param>
        /// <param name="timedOut"></param>
        private void ScheduleRetry(BackgroundJobWorkItem item, Exception exception, bool timedOut) {
            BackgroundJobRecord job = item.Job;
            TimeSpan delay = CalculateRetryDelay(job.Options, job.Attempt);
            DateTimeOffset dueAtUtc = DateTimeOffset.UtcNow.Add(delay);

            job.MarkRetrying(dueAtUtc, exception);
            SaveJob(job);
            ScheduleTimed(item, dueAtUtc);

            BackgroundTelemetry? telemetry = EnsureTelemetry();
            if (telemetry != null) {
                TagList tags = default;
                tags.Add("source", NormalizeSource(job.Source));
                tags.Add("reason", timedOut ? "timeout" : "exception");
                telemetry.RetriedTotal.Add(1, tags);
            }

            _log.Warn($"background job '{job.Name}' attempt {job.Attempt} failed; retry scheduled at {dueAtUtc:O}", exception);
        }

        #endregion worker execution and retry decisions

        #region timed job scheduling

        /// <summary>
        /// Inserts a delayed job or retry into the shared timed priority queue.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="dueAtUtc"></param>
        private void ScheduleTimed(BackgroundJobWorkItem item, DateTimeOffset dueAtUtc) {
            if (item.Job.IsTerminal) {
                return;
            }

            if (!item.Job.TrySetTimed()) {
                return;
            }

            // Each live job can own at most one timed entry.
            Interlocked.Increment(ref _scheduledJobs);

            lock (_timedLock) {
                _timedJobs.Enqueue(item, dueAtUtc);
            }
            SignalTimedScheduler();
        }

        /// <summary>
        /// Removes a canceled job from the timed queue and preserves all other priorities.
        /// </summary>
        /// <param name="id"></param>
        private void RemoveTimedJob(Guid id) {
            lock (_timedLock) {
                if (_timedJobs.Count == 0) {
                    return;
                }

                // PriorityQueue has no arbitrary remove operation, so rebuild it under the same lock.
                List<(BackgroundJobWorkItem Item, DateTimeOffset DueAtUtc)> retained = new(_timedJobs.Count);
                while (_timedJobs.TryDequeue(out BackgroundJobWorkItem? item, out DateTimeOffset dueAtUtc)) {
                    if (item != null && item.Job.Id != id) {
                        retained.Add((item, dueAtUtc));
                    }
                }

                foreach ((BackgroundJobWorkItem item, DateTimeOffset dueAtUtc) in retained) {
                    _timedJobs.Enqueue(item, dueAtUtc);
                }
            }
        }

        /// <summary>
        /// Clears every timed entry during shutdown and reconciles the scheduled-job gauge.
        /// </summary>
        private void ClearTimedJobs() {
            lock (_timedLock) {
                while (_timedJobs.TryDequeue(out BackgroundJobWorkItem? item, out _)) {
                    if (item != null && item.Job.TryClearTimed()) {
                        Interlocked.Decrement(ref _scheduledJobs);
                    }
                }
            }
        }

        /// <summary>
        /// Waits for the earliest timed entry and publishes due work to the ready channel.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task TimedSchedulerLoopAsync(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    BackgroundJobWorkItem? item = null;
                    TimeSpan? wait = null;

                    lock (_timedLock) {
                        if (_timedJobs.TryPeek(out _, out DateTimeOffset dueAtUtc)) {
                            TimeSpan remaining = dueAtUtc - DateTimeOffset.UtcNow;
                            if (remaining <= TimeSpan.Zero) {
                                item = _timedJobs.Dequeue();
                            }
                            else {
                                wait = ClampWait(remaining);
                            }
                        }
                    }

                    if (item != null) {
                        // The timed flag belongs to the priority-queue entry, not to channel admission.
                        if (item.Job.TryClearTimed()) {
                            Interlocked.Decrement(ref _scheduledJobs);
                        }
                        if (!item.Job.IsTerminal) {
                            await QueueExistingAsync(item, token).ConfigureAwait(false);
                        }
                        continue;
                    }

                    // A signal interrupts the delay when a new earlier entry is inserted.
                    if (wait.HasValue) {
                        await _timedSignal.WaitAsync(wait.Value, token).ConfigureAwait(false);
                    }
                    else {
                        await _timedSignal.WaitAsync(token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch (Exception ex) {
                    _log.Error("background timed scheduler loop error", ex);
                }
            }
        }

        #endregion timed job scheduling

        #region recurring schedules

        /// <summary>
        /// Calculates and enqueues occurrences for one cron registration.
        /// </summary>
        /// <param name="schedule"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task CronSchedulerLoopAsync(BackgroundOptions.BackgroundCronRegistration schedule, CancellationToken token) {
            CronExpression expression = schedule.GetExpression();
            TimeZoneInfo timeZone = schedule.Options.TimeZone ?? _options.TimeZone;
            int active = 0;

            while (!token.IsCancellationRequested) {
                bool releaseActiveOnError = false;
                try {
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    DateTimeOffset? next = expression.GetNextOccurrence(now, timeZone);

                    if (next == null) {
                        _log.Warn($"cron schedule '{schedule.Name}' has no next occurrence");
                        return;
                    }

                    await DelayUntilAsync(next.Value, token).ConfigureAwait(false);

                    // The active flag remains set across retries and is released only at terminal completion.
                    if (!schedule.Options.AllowConcurrentExecutions && Interlocked.CompareExchange(ref active, 1, 0) != 0) {
                        _log.Warn($"cron schedule '{schedule.Name}' skipped because previous run is still queued, running, or retrying");
                        continue;
                    }
                    releaseActiveOnError = !schedule.Options.AllowConcurrentExecutions;

                    bool enqueued = TryEnqueue(
                        schedule.Name,
                        source: "cron",
                        schedule.Work,
                        schedule.Options.JobOptions,
                        onCompleted: schedule.Options.AllowConcurrentExecutions ? null : () => Volatile.Write(ref active, 0),
                        out _
                    );

                    if (!enqueued) {
                        if (!schedule.Options.AllowConcurrentExecutions) {
                            Volatile.Write(ref active, 0);
                        }
                        releaseActiveOnError = false;
                        _log.Warn($"cron schedule '{schedule.Name}' could not enqueue because the background queue is full");
                    }
                    else {
                        releaseActiveOnError = false;
                    }
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch (Exception ex) {
                    if (releaseActiveOnError) {
                        Volatile.Write(ref active, 0);
                    }
                    _log.Error($"cron schedule '{schedule.Name}' loop error", ex);

                    // Prevent a malformed runtime condition from spinning the scheduler loop.
                    try {
                        await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                    }
                    catch {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Enqueues occurrences for one fixed-rate interval registration.
        /// </summary>
        /// <param name="schedule"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task IntervalSchedulerLoopAsync(BackgroundOptions.BackgroundIntervalRegistration schedule, CancellationToken token) {
            int active = 0;
            DateTimeOffset next = DateTimeOffset.UtcNow.Add(schedule.Interval);

            while (!token.IsCancellationRequested) {
                bool releaseActiveOnError = false;
                try {
                    await DelayUntilAsync(next, token).ConfigureAwait(false);

                    if (!schedule.Options.AllowConcurrentExecutions && Interlocked.CompareExchange(ref active, 1, 0) != 0) {
                        _log.Warn($"interval schedule '{schedule.Name}' skipped because previous run is still queued, running, or retrying");
                    }
                    else {
                        releaseActiveOnError = !schedule.Options.AllowConcurrentExecutions;
                        bool enqueued = TryEnqueue(
                            schedule.Name,
                            source: "interval",
                            schedule.Work,
                            schedule.Options.JobOptions,
                            onCompleted: schedule.Options.AllowConcurrentExecutions ? null : () => Volatile.Write(ref active, 0),
                            out _
                        );

                        if (!enqueued) {
                            if (!schedule.Options.AllowConcurrentExecutions) {
                                Volatile.Write(ref active, 0);
                            }
                            releaseActiveOnError = false;
                            _log.Warn($"interval schedule '{schedule.Name}' could not enqueue because the background queue is full");
                        }
                        else {
                            releaseActiveOnError = false;
                        }
                    }

                    // Advance from the planned tick, not from completion, to preserve fixed-rate semantics.
                    next = AdvanceInterval(next, schedule.Interval, DateTimeOffset.UtcNow);
                }
                catch (OperationCanceledException) {
                    return;
                }
                catch (Exception ex) {
                    if (releaseActiveOnError) {
                        Volatile.Write(ref active, 0);
                    }
                    _log.Error($"interval schedule '{schedule.Name}' loop error", ex);
                    next = DateTimeOffset.UtcNow.Add(schedule.Interval);
                }
            }
        }

        #endregion recurring schedules

        #region completion, retention, and snapshot storage

        /// <summary>
        /// Finalizes a job exactly once, releases schedule concurrency, records telemetry, and applies retention.
        /// </summary>
        /// <param name="job"></param>
        /// <param name="result"></param>
        private void CompleteJob(BackgroundJobRecord job, string result) {
            // Cancellation and a returning worker may both observe completion; only one owns finalization.
            if (!job.TryRegisterCompletion()) {
                return;
            }

            try {
                job.NotifyCompleted();
            }
            catch (Exception ex) {
                _log.Error($"background job '{job.Name}' completion callback failed", ex);
            }

            BackgroundTelemetry? telemetry = EnsureTelemetry();
            if (telemetry != null) {
                TagList tags = default;
                tags.Add("source", NormalizeSource(job.Source));
                tags.Add("result", result);
                telemetry.CompletedTotal.Add(1, tags);

                if (result == "canceled") {
                    telemetry.CanceledTotal.Add(1, tags);
                }
            }

            RememberCompletedJob(job.Id);
        }

        /// <summary>
        /// Retains or removes a completed job according to the configured count limit.
        /// </summary>
        /// <param name="id"></param>
        private void RememberCompletedJob(Guid id) {
            if (_options.CompletedJobRetention == 0) {
                if (_jobs.TryRemove(id, out BackgroundJobRecord? removed)) {
                    removed.Dispose();
                }
                RemoveJob(id);
                return;
            }

            _completedJobs.Enqueue(id);

            while (_completedJobs.Count > _options.CompletedJobRetention && _completedJobs.TryDequeue(out Guid oldId)) {
                if (_jobs.TryRemove(oldId, out BackgroundJobRecord? removed)) {
                    removed.Dispose();
                }
                RemoveJob(oldId);
            }
        }

        /// <summary>
        /// Persists the latest immutable snapshot without allowing store failures to stop workers.
        /// </summary>
        /// <param name="job"></param>
        private void SaveJob(BackgroundJobRecord job) {
            try {
                _jobStore.Save(job.ToSnapshot());
            }
            catch (Exception ex) {
                _log.Error($"background job store failed to save job '{job.Id}'", ex);
            }
        }

        /// <summary>
        /// Removes one retained snapshot without allowing store failures to stop workers.
        /// </summary>
        /// <param name="id"></param>
        private void RemoveJob(Guid id) {
            try {
                _jobStore.Remove(id);
            }
            catch (Exception ex) {
                _log.Error($"background job store failed to remove job '{id}'", ex);
            }
        }

        #endregion completion, retention, and snapshot storage

        #region telemetry

        /// <summary>
        /// Records successful channel admission.
        /// </summary>
        /// <param name="source"></param>
        private void RecordEnqueued(string source) {
            BackgroundTelemetry? telemetry = EnsureTelemetry();
            if (telemetry != null) {
                TagList tags = default;
                tags.Add("source", NormalizeSource(source));
                telemetry.EnqueuedTotal.Add(1, tags);
            }
        }

        /// <summary>
        /// Records immediate rejection caused by a full bounded queue.
        /// </summary>
        /// <param name="source"></param>
        private void RecordRejected(string source) {
            BackgroundTelemetry? telemetry = EnsureTelemetry();
            if (telemetry != null) {
                TagList tags = default;
                tags.Add("source", NormalizeSource(source));
                tags.Add("reason", "queue_full");
                telemetry.RejectedTotal.Add(1, tags);
            }
        }

        /// <summary>
        /// Lazily creates and returns telemetry instruments when both switches are enabled.
        /// </summary>
        /// <returns></returns>
        private BackgroundTelemetry? EnsureTelemetry() {
            if (!_options.EnableTelemetry) {
                return null;
            }

            SimpleWServer? server = _server;
            if (server == null) {
                return null;
            }

            Telemetry? telemetry = server.Telemetry;
            if (telemetry == null) {
                return null;
            }

            BackgroundTelemetry? t = _telemetry;
            if (t != null) {
                return t;
            }

            // Double-checked locking avoids repeated instrument registration.
            lock (_telemetryLock) {
                _telemetry ??= new BackgroundTelemetry(telemetry.Meter, this);
                return _telemetry;
            }
        }

        /// <summary>
        /// Clears instruments associated with the completed server lifetime.
        /// </summary>
        internal void ResetTelemetry() {
            lock (_telemetryLock) {
                _telemetry = null;
            }
        }

        #endregion telemetry

        #region configuration and timing helpers

        /// <summary>
        /// Clones global defaults, applies the per-job override, and normalizes the result.
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        private BackgroundJobOptions ResolveJobOptions(Action<BackgroundJobOptions>? configure) {
            BackgroundJobOptions options = _options.DefaultJobOptions.Clone();
            configure?.Invoke(options);
            return options.ValidateAndNormalize();
        }

        /// <summary>
        /// Returns the current service-stop token under the lifetime lock.
        /// </summary>
        /// <returns></returns>
        private CancellationToken GetStopToken() {
            lock (_lifetimeLock) {
                return _stopCts?.Token ?? CancellationToken.None;
            }
        }

        /// <summary>
        /// Rejects new work after shutdown has begun.
        /// </summary>
        private void ThrowIfStopping() {
            if (Volatile.Read(ref _stopping)) {
                throw new InvalidOperationException("Background service is stopping.");
            }
        }

        /// <summary>
        /// Wakes the timed scheduler without accumulating redundant semaphore signals.
        /// </summary>
        private void SignalTimedScheduler() {
            try {
                if (_timedSignal.CurrentCount == 0) {
                    _timedSignal.Release();
                }
            }
            catch (SemaphoreFullException) { }
        }

        /// <summary>
        /// Delays until an absolute UTC date while keeping each individual wait within Task.Delay limits.
        /// </summary>
        /// <param name="dueAtUtc"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private static async Task DelayUntilAsync(DateTimeOffset dueAtUtc, CancellationToken token) {
            while (true) {
                TimeSpan delay = dueAtUtc - DateTimeOffset.UtcNow;
                if (delay <= TimeSpan.Zero) {
                    return;
                }
                await Task.Delay(ClampWait(delay), token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Limits one wait segment so very distant dates remain safe for Task.Delay.
        /// </summary>
        /// <param name="delay"></param>
        /// <returns></returns>
        private static TimeSpan ClampWait(TimeSpan delay) {
            TimeSpan maximum = TimeSpan.FromDays(1);
            return delay > maximum ? maximum : delay;
        }

        /// <summary>
        /// Advances a fixed-rate schedule past every missed tick without drifting from its cadence.
        /// </summary>
        /// <param name="previous"></param>
        /// <param name="interval"></param>
        /// <param name="now"></param>
        /// <returns></returns>
        private static DateTimeOffset AdvanceInterval(DateTimeOffset previous, TimeSpan interval, DateTimeOffset now) {
            DateTimeOffset next = previous.Add(interval);
            if (next > now) {
                return next;
            }

            long missed = ((now - next).Ticks / interval.Ticks) + 1;
            return next.AddTicks(missed * interval.Ticks);
        }

        /// <summary>
        /// Computes deterministic exponential backoff capped by the configured maximum delay.
        /// </summary>
        /// <param name="options"></param>
        /// <param name="attempt"></param>
        /// <returns></returns>
        private static TimeSpan CalculateRetryDelay(BackgroundJobOptions options, int attempt) {
            double delayMs = options.RetryDelay.TotalMilliseconds * Math.Pow(options.RetryBackoffFactor, Math.Max(0, attempt - 1));
            delayMs = Math.Min(delayMs, options.RetryMaxDelay.TotalMilliseconds);

            return TimeSpan.FromMilliseconds(Math.Max(0d, delayMs));
        }

        /// <summary>
        /// Reduces arbitrary sources to the low-cardinality values accepted by telemetry.
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private static string NormalizeSource(string? source) {
            if (string.IsNullOrWhiteSpace(source)) {
                return "unknown";
            }

            string normalized = source.Trim().ToLowerInvariant();
            return normalized is "handler" or "cron" or "interval" ? normalized : "unknown";
        }

        #endregion configuration and timing helpers

        #region internal work and telemetry types

        /// <summary>
        /// Couples a mutable job record with the delegate executed by workers.
        /// </summary>
        /// <param name="Job"></param>
        /// <param name="Work"></param>
        private sealed record BackgroundJobWorkItem(BackgroundJobRecord Job, Func<BackgroundJobContext, Task> Work);

        /// <summary>
        /// Holds all OpenTelemetry instruments owned by one background service instance.
        /// </summary>
        private sealed class BackgroundTelemetry {

            /// <summary>Jobs admitted to the ready channel.</summary>
            public readonly Counter<long> EnqueuedTotal;
            /// <summary>Jobs rejected because the ready channel was full.</summary>
            public readonly Counter<long> RejectedTotal;
            /// <summary>Jobs that started their first attempt.</summary>
            public readonly Counter<long> StartedTotal;
            /// <summary>All attempts that started, including retries.</summary>
            public readonly Counter<long> AttemptedTotal;
            /// <summary>All attempts that completed.</summary>
            public readonly Counter<long> AttemptCompletedTotal;
            /// <summary>Jobs that reached a terminal state.</summary>
            public readonly Counter<long> CompletedTotal;
            /// <summary>Progress reports emitted by running jobs.</summary>
            public readonly Counter<long> ProgressTotal;
            /// <summary>Retry attempts scheduled after a failure or timeout.</summary>
            public readonly Counter<long> RetriedTotal;
            /// <summary>Attempts that exceeded their configured timeout.</summary>
            public readonly Counter<long> TimedOutTotal;
            /// <summary>Jobs canceled before terminal completion.</summary>
            public readonly Counter<long> CanceledTotal;
            /// <summary>Time spent waiting in the ready channel.</summary>
            public readonly Histogram<double> QueueWaitDurationMs;
            /// <summary>Delegate execution duration per attempt.</summary>
            public readonly Histogram<double> RunDurationMs;

            /// <summary>
            /// Creates counters, histograms, and observable gauges for one service instance.
            /// </summary>
            /// <param name="meter"></param>
            /// <param name="service"></param>
            public BackgroundTelemetry(Meter meter, BackgroundService service) {
                EnqueuedTotal = meter.CreateCounter<long>("simplew.background.job.enqueued.count", unit: "job");
                RejectedTotal = meter.CreateCounter<long>("simplew.background.job.rejected.count", unit: "job");
                StartedTotal = meter.CreateCounter<long>("simplew.background.job.started.count", unit: "job");
                AttemptedTotal = meter.CreateCounter<long>("simplew.background.job.attempted.count", unit: "attempt");
                AttemptCompletedTotal = meter.CreateCounter<long>("simplew.background.job.attempt.completed.count", unit: "attempt");
                CompletedTotal = meter.CreateCounter<long>("simplew.background.job.completed.count", unit: "job");
                ProgressTotal = meter.CreateCounter<long>("simplew.background.job.progress.count", unit: "report");
                RetriedTotal = meter.CreateCounter<long>("simplew.background.job.retried.count", unit: "retry");
                TimedOutTotal = meter.CreateCounter<long>("simplew.background.job.timed_out.count", unit: "attempt");
                CanceledTotal = meter.CreateCounter<long>("simplew.background.job.canceled.count", unit: "job");
                QueueWaitDurationMs = meter.CreateHistogram<double>("simplew.background.job.queue_wait.duration", unit: "ms");
                RunDurationMs = meter.CreateHistogram<double>("simplew.background.job.run.duration", unit: "ms");

                meter.CreateObservableGauge<int>("simplew.background.queue.length", () => Volatile.Read(ref service._queueLength), unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.queue.capacity", () => service._options.Capacity, unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.job.running", () => Volatile.Read(ref service._runningJobs), unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.job.scheduled", () => Volatile.Read(ref service._scheduledJobs), unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.job.tracked", () => service._jobs.Count, unit: "job");
                meter.CreateObservableGauge<int>("simplew.background.worker.count", () => service._options.WorkerCount, unit: "worker");
                meter.CreateObservableGauge<int>(
                    "simplew.background.cron.schedule.enabled",
                    () => service._options.Schedules.Count(static schedule => schedule.Options.Enabled),
                    unit: "schedule"
                );
                meter.CreateObservableGauge<int>(
                    "simplew.background.interval.schedule.enabled",
                    () => service._options.Intervals.Count(static schedule => schedule.Options.Enabled),
                    unit: "schedule"
                );
            }

        }

        #endregion internal work and telemetry types

    }

    /// <summary>
    /// Background Job Record
    /// </summary>
    internal sealed class BackgroundJobRecord : IDisposable {

        #region synchronization and immutable job data

        /// <summary>
        /// Protects every mutable state transition and snapshot read.
        /// </summary>
        private readonly object _sync = new();

        /// <summary>
        /// Individual cancellation source exposed to the running delegate.
        /// </summary>
        private readonly CancellationTokenSource _cancellation = new();

        /// <summary>
        /// Optional callback used by recurring schedules to release their concurrency guard.
        /// </summary>
        private readonly Action? _onCompleted;

        /// <summary>Unique identifier preserved across every attempt.</summary>
        public Guid Id { get; }
        /// <summary>Human-readable job name.</summary>
        public string Name { get; }
        /// <summary>Normalized origin used by snapshots and telemetry.</summary>
        public string Source { get; }
        /// <summary>UTC date at which the job record was created.</summary>
        public DateTimeOffset EnqueuedAtUtc { get; }
        /// <summary>Validated execution options owned by this job.</summary>
        public BackgroundJobOptions Options { get; }
        /// <summary>Token canceled by individual cancellation or service shutdown linkage.</summary>
        public CancellationToken CancellationToken => _cancellation.Token;

        #endregion synchronization and immutable job data

        #region mutable state

        /// <summary>Current observable lifecycle state.</summary>
        private BackgroundJobStatus _status;
        /// <summary>Original due date for a delayed job.</summary>
        private DateTimeOffset? _scheduledAtUtc;
        /// <summary>Due date of the currently pending retry.</summary>
        private DateTimeOffset? _nextAttemptAtUtc;
        /// <summary>Most recent date at which the job entered the ready channel.</summary>
        private DateTimeOffset _queuedAtUtc;
        /// <summary>Date at which the first attempt started.</summary>
        private DateTimeOffset? _startedAtUtc;
        /// <summary>Date at which the job entered a terminal state.</summary>
        private DateTimeOffset? _finishedAtUtc;
        /// <summary>Terminal error exposed by the snapshot.</summary>
        private string? _error;
        /// <summary>Error produced by the most recent failed attempt.</summary>
        private string? _lastError;
        /// <summary>Latest normalized progress percentage.</summary>
        private double? _progress;
        /// <summary>Latest progress message.</summary>
        private string? _progressMessage;
        /// <summary>Date of the latest state or progress change.</summary>
        private DateTimeOffset? _updatedAtUtc;
        /// <summary>Number of attempts that have started.</summary>
        private int _attempt;
        /// <summary>Atomic guard ensuring completion side effects run once.</summary>
        private int _completionRegistered;
        /// <summary>Atomic flag indicating ownership of one timed-queue entry.</summary>
        private int _timed;
        /// <summary>Indicates that individual cancellation has been requested.</summary>
        private bool _cancellationRequested;

        #endregion mutable state

        #region derived state

        /// <summary>
        /// Gets the current or last completed attempt number.
        /// </summary>
        public int Attempt {
            get {
                lock (_sync) {
                    return _attempt;
                }
            }
        }

        /// <summary>
        /// Gets whether another attempt is allowed by the retry policy.
        /// </summary>
        public bool CanRetry {
            get {
                lock (_sync) {
                    return !_cancellationRequested && _attempt <= Options.RetryCount;
                }
            }
        }

        /// <summary>
        /// Gets whether individual cancellation has been requested.
        /// </summary>
        public bool IsCancellationRequested {
            get {
                lock (_sync) {
                    return _cancellationRequested;
                }
            }
        }

        /// <summary>
        /// Gets whether the job has reached a final state.
        /// </summary>
        public bool IsTerminal {
            get {
                lock (_sync) {
                    return IsTerminalStatus(_status);
                }
            }
        }

        #endregion derived state

        #region construction and projections

        /// <summary>
        /// Creates a tracked job in either queued or scheduled state.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="name"></param>
        /// <param name="source"></param>
        /// <param name="enqueuedAtUtc"></param>
        /// <param name="scheduledAtUtc"></param>
        /// <param name="options"></param>
        /// <param name="onCompleted"></param>
        public BackgroundJobRecord(Guid id, string name, string source, DateTimeOffset enqueuedAtUtc, DateTimeOffset? scheduledAtUtc, BackgroundJobOptions options, Action? onCompleted) {
            Id = id;
            Name = name;
            Source = source;
            EnqueuedAtUtc = enqueuedAtUtc;
            Options = options;
            _onCompleted = onCompleted;
            _scheduledAtUtc = scheduledAtUtc;
            _queuedAtUtc = enqueuedAtUtc;
            _status = scheduledAtUtc.HasValue ? BackgroundJobStatus.Scheduled : BackgroundJobStatus.Queued;
            _updatedAtUtc = enqueuedAtUtc;
        }

        /// <summary>
        /// Creates the lightweight public handle returned to the caller.
        /// </summary>
        /// <returns></returns>
        public BackgroundJobHandle ToHandle() => new(Id, Name, EnqueuedAtUtc);

        #endregion construction and projections

        #region lifecycle transitions

        /// <summary>
        /// Publishes the channel item while holding the state lock and rolls back state if publication fails.
        /// </summary>
        /// <param name="queuedAtUtc"></param>
        /// <param name="publish"></param>
        /// <returns></returns>
        public bool TryPublishQueued(DateTimeOffset queuedAtUtc, Func<bool> publish) {
            lock (_sync) {
                if (IsTerminalStatus(_status)) {
                    return false;
                }

                BackgroundJobStatus previousStatus = _status;
                DateTimeOffset previousQueuedAtUtc = _queuedAtUtc;
                DateTimeOffset? previousNextAttemptAtUtc = _nextAttemptAtUtc;

                _status = BackgroundJobStatus.Queued;
                _queuedAtUtc = queuedAtUtc;
                _nextAttemptAtUtc = null;
                _updatedAtUtc = queuedAtUtc;

                // Publication occurs under the same lock as the state transition to prevent a fast-worker race.
                if (publish()) {
                    return true;
                }

                _status = previousStatus;
                _queuedAtUtc = previousQueuedAtUtc;
                _nextAttemptAtUtc = previousNextAttemptAtUtc;
                _updatedAtUtc = DateTimeOffset.UtcNow;
                return false;
            }
        }

        /// <summary>
        /// Transitions a queued job to running and increments its attempt number.
        /// </summary>
        /// <param name="startedAtUtc"></param>
        /// <param name="attempt"></param>
        /// <param name="queuedAtUtc"></param>
        /// <returns></returns>
        public bool TryBeginAttempt(DateTimeOffset startedAtUtc, out int attempt, out DateTimeOffset queuedAtUtc) {
            lock (_sync) {
                if (IsTerminalStatus(_status) || _cancellationRequested) {
                    attempt = _attempt;
                    queuedAtUtc = _queuedAtUtc;
                    return false;
                }

                _attempt++;
                attempt = _attempt;
                queuedAtUtc = _queuedAtUtc;
                _status = BackgroundJobStatus.Running;
                _startedAtUtc ??= startedAtUtc;
                _finishedAtUtc = null;
                _error = null;
                _nextAttemptAtUtc = null;
                _updatedAtUtc = startedAtUtc;
                return true;
            }
        }

        /// <summary>
        /// Moves a failed attempt to retrying and resets attempt-scoped progress.
        /// </summary>
        /// <param name="nextAttemptAtUtc"></param>
        /// <param name="exception"></param>
        public void MarkRetrying(DateTimeOffset nextAttemptAtUtc, Exception exception) {
            lock (_sync) {
                _status = BackgroundJobStatus.Retrying;
                _nextAttemptAtUtc = nextAttemptAtUtc;
                _lastError = exception.Message;
                _progress = null;
                _progressMessage = null;
                _updatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        /// <summary>
        /// Marks the job successful and completes known progress at one hundred percent.
        /// </summary>
        /// <param name="finishedAtUtc"></param>
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

        /// <summary>
        /// Marks the job canceled after a running delegate returns.
        /// </summary>
        /// <param name="finishedAtUtc"></param>
        public void MarkCanceled(DateTimeOffset finishedAtUtc) {
            lock (_sync) {
                _status = BackgroundJobStatus.Canceled;
                _finishedAtUtc = finishedAtUtc;
                _error = null;
                _nextAttemptAtUtc = null;
                _updatedAtUtc = finishedAtUtc;
            }
        }

        /// <summary>
        /// Marks the job failed after exhausting its retry policy.
        /// </summary>
        /// <param name="finishedAtUtc"></param>
        /// <param name="exception"></param>
        public void MarkFailed(DateTimeOffset finishedAtUtc, Exception exception) {
            lock (_sync) {
                _status = BackgroundJobStatus.Failed;
                _finishedAtUtc = finishedAtUtc;
                _error = exception.Message;
                _lastError = exception.Message;
                _nextAttemptAtUtc = null;
                _updatedAtUtc = finishedAtUtc;
            }
        }

        /// <summary>
        /// Marks the job timed out when no additional timeout retry is allowed.
        /// </summary>
        /// <param name="finishedAtUtc"></param>
        /// <param name="exception"></param>
        public void MarkTimedOut(DateTimeOffset finishedAtUtc, Exception exception) {
            lock (_sync) {
                _status = BackgroundJobStatus.TimedOut;
                _finishedAtUtc = finishedAtUtc;
                _error = exception.Message;
                _lastError = exception.Message;
                _nextAttemptAtUtc = null;
                _updatedAtUtc = finishedAtUtc;
            }
        }

        /// <summary>
        /// Requests cooperative cancellation and immediately completes jobs that are not running.
        /// </summary>
        /// <param name="finishedAtUtc"></param>
        /// <param name="completedNow"></param>
        /// <returns></returns>
        public bool RequestCancellation(DateTimeOffset finishedAtUtc, out bool completedNow) {
            bool cancelToken = false;

            lock (_sync) {
                if (IsTerminalStatus(_status)) {
                    completedNow = false;
                    return false;
                }

                if (!_cancellationRequested) {
                    _cancellationRequested = true;
                    cancelToken = true;
                }

                // A running delegate owns its final transition when it returns from user code.
                completedNow = _status != BackgroundJobStatus.Running;
                if (completedNow) {
                    _status = BackgroundJobStatus.Canceled;
                    _finishedAtUtc = finishedAtUtc;
                    _error = null;
                    _nextAttemptAtUtc = null;
                    _updatedAtUtc = finishedAtUtc;
                }
            }

            if (cancelToken) {
                try { _cancellation.Cancel(); }
                catch { }
            }
            return true;
        }

        /// <summary>
        /// Updates progress only while user code is actively running.
        /// </summary>
        /// <param name="percent"></param>
        /// <param name="message"></param>
        public void ReportProgress(double? percent, string? message) {
            lock (_sync) {
                if (_status != BackgroundJobStatus.Running) {
                    return;
                }
                if (percent.HasValue) {
                    _progress = NormalizePercent(percent.Value);
                }
                if (message != null) {
                    _progressMessage = message;
                }
                _updatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        #endregion lifecycle transitions

        #region atomic guards and completion

        /// <summary>
        /// Claims ownership of one timed priority-queue entry.
        /// </summary>
        /// <returns></returns>
        public bool TrySetTimed() => Interlocked.Exchange(ref _timed, 1) == 0;

        /// <summary>
        /// Releases ownership of one timed priority-queue entry.
        /// </summary>
        /// <returns></returns>
        public bool TryClearTimed() => Interlocked.Exchange(ref _timed, 0) == 1;

        /// <summary>
        /// Claims terminal completion side effects exactly once.
        /// </summary>
        /// <returns></returns>
        public bool TryRegisterCompletion() => Interlocked.Exchange(ref _completionRegistered, 1) == 0;

        /// <summary>
        /// Invokes the optional recurring-schedule completion callback.
        /// </summary>
        public void NotifyCompleted() => _onCompleted?.Invoke();

        #endregion atomic guards and completion

        #region snapshot and disposal

        /// <summary>
        /// Creates an immutable, internally consistent view of the current job state.
        /// </summary>
        /// <returns></returns>
        public BackgroundJobSnapshot ToSnapshot() {
            lock (_sync) {
                return new BackgroundJobSnapshot(Id, Name, Source, _status, EnqueuedAtUtc, _startedAtUtc, _finishedAtUtc, _error, _progress, _progressMessage, _updatedAtUtc) {
                    ScheduledAtUtc = _scheduledAtUtc,
                    NextAttemptAtUtc = _nextAttemptAtUtc,
                    Attempt = _attempt,
                    MaxAttempts = Options.RetryCount + 1,
                    LastError = _lastError,
                    Timeout = Options.Timeout
                };
            }
        }

        /// <summary>
        /// Releases the individual cancellation source after retention removes the job.
        /// </summary>
        public void Dispose() {
            _cancellation.Dispose();
        }

        #endregion snapshot and disposal

        #region state helpers

        /// <summary>
        /// Determines whether a status forbids every subsequent transition.
        /// </summary>
        /// <param name="status"></param>
        /// <returns></returns>
        private static bool IsTerminalStatus(BackgroundJobStatus status) {
            return status is BackgroundJobStatus.Succeeded
                or BackgroundJobStatus.Failed
                or BackgroundJobStatus.TimedOut
                or BackgroundJobStatus.Canceled;
        }

        /// <summary>
        /// Converts invalid progress values to null and clamps valid values to the 0..100 range.
        /// </summary>
        /// <param name="percent"></param>
        /// <returns></returns>
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

        #endregion state helpers

    }

}
