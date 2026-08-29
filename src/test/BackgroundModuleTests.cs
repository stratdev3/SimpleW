using System.Net;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Cronos;
using NFluent;
using SimpleW;
using SimpleW.Service.Background;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for SimpleW.Service.Background.
    /// </summary>
    public class BackgroundModuleTests {

        [Fact]
        public void GetBackgroundService_Without_Module_Should_Throw() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            try {
                Assert.Throws<InvalidOperationException>(() => server.GetBackgroundService());
            }
            finally {
            }
        }

        [Fact]
        public void UseBackgroundModule_Twice_Should_Throw() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseBackgroundModule();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => server.UseBackgroundModule());

            Check.That(exception.Message).Contains("already installed");
        }

        [Fact]
        public void TryEnqueue_Should_Return_False_When_Queue_Is_Full() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule(options => {
                options.Capacity = 1;
            });

            try {
                IBackgroundService background = server.GetBackgroundService();

                bool first = background.TryEnqueue("first", _ => Task.CompletedTask, out BackgroundJobHandle firstHandle);
                bool second = background.TryEnqueue("second", _ => Task.CompletedTask, out BackgroundJobHandle secondHandle);

                Check.That(first).IsTrue();
                Check.That(firstHandle.Id).IsNotEqualTo(Guid.Empty);
                Check.That(second).IsFalse();
                Check.That(secondHandle.Id).IsEqualTo(Guid.Empty);
            }
            finally {
            }
        }

        [Fact]
        public void ScheduleEvery_Should_Reject_Non_Positive_Intervals() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            Assert.Throws<ArgumentOutOfRangeException>(() => server.UseBackgroundModule(options => {
                options.ScheduleEvery("invalid", TimeSpan.Zero, _ => Task.CompletedTask);
            }));
        }

        [Fact]
        public async Task Telemetry_Should_Record_Background_Counters() {
            ConcurrentDictionary<string, long> counters = new();

            using MeterListener listener = new();
            listener.InstrumentPublished = (instrument, meterListener) => {
                if (instrument.Meter.Name == "SimpleW" && instrument.Name.StartsWith("simplew.background.", StringComparison.Ordinal)) {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => {
                counters.AddOrUpdate(instrument.Name, measurement, (_, current) => current + measurement);
            });
            listener.SetMeasurementEventCallback<double>((_, _, _, _) => { });
            listener.Start();

            TaskCompletionSource<bool> firstJobStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseFirstJob = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.ConfigureTelemetry(options => options.Enabled = true);
            server.UseBackgroundModule(options => {
                options.EnableTelemetry = true;
                options.Capacity = 1;
            });

            try {
                IBackgroundService background = server.GetBackgroundService();

                await server.StartAsync();

                bool first = background.TryEnqueue("telemetry-job", async ctx => {
                    ctx.ReportProgress(50, "half");
                    firstJobStarted.TrySetResult(true);
                    await releaseFirstJob.Task;
                }, out BackgroundJobHandle firstHandle);

                await firstJobStarted.Task;

                bool second = background.TryEnqueue("queued-job", _ => Task.CompletedTask, out BackgroundJobHandle secondHandle);
                bool third = background.TryEnqueue("rejected-job", _ => Task.CompletedTask, out BackgroundJobHandle thirdHandle);

                releaseFirstJob.TrySetResult(true);

                Check.That(first).IsTrue();
                Check.That(firstHandle.Id).IsNotEqualTo(Guid.Empty);
                Check.That(second).IsTrue();
                Check.That(secondHandle.Id).IsNotEqualTo(Guid.Empty);
                Check.That(third).IsFalse();
                Check.That(thirdHandle.Id).IsEqualTo(Guid.Empty);

                await WaitForStatusAsync(background, firstHandle.Id, BackgroundJobStatus.Succeeded);
                await WaitForStatusAsync(background, secondHandle.Id, BackgroundJobStatus.Succeeded);
                await WaitForMetricAsync(() => CounterValue(counters, "simplew.background.job.completed.count") == 2);

                Check.That(CounterValue(counters, "simplew.background.job.enqueued.count")).IsEqualTo(2);
                Check.That(CounterValue(counters, "simplew.background.job.rejected.count")).IsEqualTo(1);
                Check.That(CounterValue(counters, "simplew.background.job.started.count")).IsEqualTo(2);
                Check.That(CounterValue(counters, "simplew.background.job.attempted.count")).IsEqualTo(2);
                Check.That(CounterValue(counters, "simplew.background.job.completed.count")).IsEqualTo(2);
                Check.That(CounterValue(counters, "simplew.background.job.progress.count")).IsEqualTo(1);

                await server.StopAsync();
                await server.StartAsync();

                BackgroundJobHandle restartedHandle = background.Enqueue("restarted-telemetry-job", _ => Task.CompletedTask);
                await WaitForStatusAsync(background, restartedHandle.Id, BackgroundJobStatus.Succeeded);
                await WaitForMetricAsync(() => CounterValue(counters, "simplew.background.job.completed.count") == 3);

                Check.That(CounterValue(counters, "simplew.background.job.enqueued.count")).IsEqualTo(3);
                Check.That(CounterValue(counters, "simplew.background.job.started.count")).IsEqualTo(3);
                Check.That(CounterValue(counters, "simplew.background.job.attempted.count")).IsEqualTo(3);
                Check.That(CounterValue(counters, "simplew.background.job.completed.count")).IsEqualTo(3);
            }
            finally {
                releaseFirstJob.TrySetResult(true);
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Custom_JobStore_Should_Receive_Job_Snapshots() {
            RecordingBackgroundJobStore store = new();

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule(options => {
                options.JobStore = store;
            });

            try {
                await server.StartAsync();

                BackgroundJobHandle job = server.GetBackgroundService().Enqueue("stored-job", ctx => {
                    ctx.ReportProgress(33, "stored progress");
                    return Task.CompletedTask;
                });

                BackgroundJobSnapshot succeeded = await WaitForStatusAsync(server.GetBackgroundService(), job.Id, BackgroundJobStatus.Succeeded);

                Check.That(succeeded.Progress).IsEqualTo(100d);
                Check.That(succeeded.ProgressMessage).IsEqualTo("stored progress");
                Check.That(store.SaveCount).IsStrictlyGreaterThan(1);
                Check.That(store.TryGet(job.Id, out BackgroundJobSnapshot? stored)).IsTrue();
                Check.That(stored!.Status).IsEqualTo(BackgroundJobStatus.Succeeded);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Handler_Should_Return_Immediately_And_Run_Job_In_Background() {
            TaskCompletionSource<bool> jobStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseJob = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<Guid> jobIdSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            server.MapGet("/api/background", (HttpSession session) => {
                BackgroundJobHandle job = session.GetBackgroundService().Enqueue("long-handler-work", async ctx => {
                    jobStarted.TrySetResult(true);
                    await releaseJob.Task.WaitAsync(ctx.CancellationToken).ConfigureAwait(false);
                });

                jobIdSource.TrySetResult(job.Id);
                return session.Response.Status(202).Text(job.Id.ToString("N"));
            });

            try {
                await server.StartAsync();

                using var client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/background");
                string content = await response.Content.ReadAsStringAsync();
                Guid jobId = await jobIdSource.Task.WaitAsync(TimeSpan.FromSeconds(2));

                Check.That(response.StatusCode).Is(HttpStatusCode.Accepted);
                Check.That(content).IsEqualTo(jobId.ToString("N"));
                Check.That(await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsTrue();

                BackgroundJobSnapshot? running = server.GetBackgroundService().GetJob(jobId);
                Check.That(running).IsNotNull();
                Check.That(running!.Status).IsEqualTo(BackgroundJobStatus.Running);

                releaseJob.TrySetResult(true);

                await WaitForStatusAsync(server.GetBackgroundService(), jobId, BackgroundJobStatus.Succeeded);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task ReportProgress_Should_Update_Job_Snapshot() {
            TaskCompletionSource<bool> progressReported = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseJob = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<Guid> jobIdSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            server.MapGet("/api/progress", (HttpSession session) => {
                BackgroundJobHandle job = session.GetBackgroundService().Enqueue("progress-job", async ctx => {
                    ctx.ReportProgress(12.5, "Parsing file");
                    ctx.ReportProgress("Waiting for release");
                    progressReported.TrySetResult(true);

                    await releaseJob.Task.WaitAsync(ctx.CancellationToken).ConfigureAwait(false);
                });

                jobIdSource.TrySetResult(job.Id);
                return session.Response.Status(202).Text(job.Id.ToString("N"));
            });

            try {
                await server.StartAsync();

                using var client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/progress");
                Guid jobId = await jobIdSource.Task.WaitAsync(TimeSpan.FromSeconds(2));

                Check.That(response.StatusCode).Is(HttpStatusCode.Accepted);
                Check.That(await progressReported.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsTrue();

                BackgroundJobSnapshot? running = server.GetBackgroundService().GetJob(jobId);
                Check.That(running).IsNotNull();
                Check.That(running!.Status).IsEqualTo(BackgroundJobStatus.Running);
                Check.That(running.Progress).IsEqualTo(12.5);
                Check.That(running.ProgressMessage).IsEqualTo("Waiting for release");
                Check.That(running.UpdatedAtUtc).IsNotNull();

                releaseJob.TrySetResult(true);

                BackgroundJobSnapshot succeeded = await WaitForStatusAsync(server.GetBackgroundService(), jobId, BackgroundJobStatus.Succeeded);
                Check.That(succeeded.Progress).IsEqualTo(100d);
                Check.That(succeeded.ProgressMessage).IsEqualTo("Waiting for release");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Failed_Job_Should_Keep_Failed_Snapshot() {
            TaskCompletionSource<Guid> jobIdSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            server.MapGet("/api/fail", (HttpSession session) => {
                BackgroundJobHandle job = session.GetBackgroundService().Enqueue("failing-job", _ => {
                    throw new InvalidOperationException("boom");
                });

                jobIdSource.TrySetResult(job.Id);
                return session.Response.Status(202).Text(job.Id.ToString("N"));
            });

            try {
                await server.StartAsync();

                using var client = new HttpClient();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/fail");
                Guid jobId = await jobIdSource.Task.WaitAsync(TimeSpan.FromSeconds(2));

                Check.That(response.StatusCode).Is(HttpStatusCode.Accepted);

                BackgroundJobSnapshot failed = await WaitForStatusAsync(server.GetBackgroundService(), jobId, BackgroundJobStatus.Failed);
                Check.That(failed.Error).Contains("boom");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Cron_Schedule_Should_Enqueue_And_Run_Job() {
            TaskCompletionSource<bool> cronRan = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule(options => {
                options.Schedule(
                    "test-cron",
                    "*/1 * * * * *",
                    _ => {
                        cronRan.TrySetResult(true);
                        return Task.CompletedTask;
                    },
                    cron => {
                        cron.CronFormat = CronFormat.IncludeSeconds;
                    }
                );
            });

            try {
                await server.StartAsync();

                bool ran = await cronRan.Task.WaitAsync(TimeSpan.FromSeconds(4));
                Check.That(ran).IsTrue();
                Check.That(server.GetBackgroundService().GetJobs().Any(job => job.Name == "test-cron" && job.Source == "cron")).IsTrue();
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task EnqueueAfter_Should_Wait_Until_Due_Date() {
            TaskCompletionSource<bool> ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();

                BackgroundJobHandle handle = server.GetBackgroundService().EnqueueAfter(
                    "delayed-job",
                    TimeSpan.FromMilliseconds(200),
                    _ => {
                        ran.TrySetResult(true);
                        return Task.CompletedTask;
                    }
                );

                BackgroundJobSnapshot? scheduled = server.GetBackgroundService().GetJob(handle.Id);
                Check.That(scheduled).IsNotNull();
                Check.That(scheduled!.Status).IsEqualTo(BackgroundJobStatus.Scheduled);
                Check.That(scheduled.ScheduledAtUtc).IsNotNull();

                await Task.Delay(50);
                Check.That(ran.Task.IsCompleted).IsFalse();

                Check.That(await ran.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsTrue();
                await WaitForStatusAsync(server.GetBackgroundService(), handle.Id, BackgroundJobStatus.Succeeded);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task EnqueueAt_In_The_Past_Should_Run_Immediately() {
            TaskCompletionSource<bool> ran = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();

                BackgroundJobHandle handle = server.GetBackgroundService().EnqueueAt(
                    "past-job",
                    DateTimeOffset.UtcNow.AddMinutes(-1),
                    _ => {
                        ran.TrySetResult(true);
                        return Task.CompletedTask;
                    }
                );

                Check.That(await ran.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsTrue();
                await WaitForStatusAsync(server.GetBackgroundService(), handle.Id, BackgroundJobStatus.Succeeded);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task EnqueueAsync_Should_Wait_For_Queue_Capacity() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule(options => {
                options.Capacity = 1;
            });

            try {
                IBackgroundService background = server.GetBackgroundService();
                background.Enqueue("first", _ => Task.CompletedTask);

                Task<BackgroundJobHandle> waiting = background.EnqueueAsync("second", _ => Task.CompletedTask).AsTask();
                await Task.Delay(50);
                Check.That(waiting.IsCompleted).IsFalse();

                await server.StartAsync();

                BackgroundJobHandle second = await waiting.WaitAsync(TimeSpan.FromSeconds(2));
                await WaitForStatusAsync(background, second.Id, BackgroundJobStatus.Succeeded);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Canceled_EnqueueAsync_Should_Not_Leave_A_Snapshot() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule(options => {
                options.Capacity = 1;
            });

            try {
                IBackgroundService background = server.GetBackgroundService();
                background.Enqueue("occupying-job", _ => Task.CompletedTask);

                using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => background.EnqueueAsync("canceled-wait", _ => Task.CompletedTask, cancellationToken: cancellation.Token).AsTask()
                );

                Check.That(background.GetJobs().Any(job => job.Name == "canceled-wait")).IsFalse();
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Retry_Should_Keep_The_Same_Job_And_Eventually_Succeed() {
            int attempts = 0;

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();

                BackgroundJobHandle handle = server.GetBackgroundService().Enqueue(
                    "retry-job",
                    _ => {
                        int attempt = Interlocked.Increment(ref attempts);
                        if (attempt < 3) {
                            throw new InvalidOperationException($"boom-{attempt}");
                        }
                        return Task.CompletedTask;
                    },
                    options => {
                        options.RetryCount = 2;
                        options.RetryDelay = TimeSpan.FromMilliseconds(20);
                    }
                );

                BackgroundJobSnapshot succeeded = await WaitForStatusAsync(server.GetBackgroundService(), handle.Id, BackgroundJobStatus.Succeeded);
                Check.That(attempts).IsEqualTo(3);
                Check.That(succeeded.Id).IsEqualTo(handle.Id);
                Check.That(succeeded.Attempt).IsEqualTo(3);
                Check.That(succeeded.MaxAttempts).IsEqualTo(3);
                Check.That(succeeded.LastError).Contains("boom-2");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Exhausted_Retries_Should_End_As_Failed() {
            int attempts = 0;

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();

                BackgroundJobHandle handle = server.GetBackgroundService().Enqueue(
                    "exhausted-job",
                    _ => {
                        Interlocked.Increment(ref attempts);
                        throw new InvalidOperationException("always fails");
                    },
                    options => {
                        options.RetryCount = 1;
                        options.RetryDelay = TimeSpan.Zero;
                    }
                );

                BackgroundJobSnapshot failed = await WaitForStatusAsync(server.GetBackgroundService(), handle.Id, BackgroundJobStatus.Failed);
                Check.That(attempts).IsEqualTo(2);
                Check.That(failed.Attempt).IsEqualTo(2);
                Check.That(failed.Error).Contains("always fails");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Timeout_Should_Retry_When_Enabled() {
            int attempts = 0;

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();

                BackgroundJobHandle handle = server.GetBackgroundService().Enqueue(
                    "timeout-retry-job",
                    async ctx => {
                        if (Interlocked.Increment(ref attempts) == 1) {
                            await Task.Delay(Timeout.InfiniteTimeSpan, ctx.CancellationToken);
                        }
                    },
                    options => {
                        options.Timeout = TimeSpan.FromMilliseconds(50);
                        options.RetryCount = 1;
                        options.RetryDelay = TimeSpan.Zero;
                        options.RetryOnTimeout = true;
                    }
                );

                BackgroundJobSnapshot succeeded = await WaitForStatusAsync(server.GetBackgroundService(), handle.Id, BackgroundJobStatus.Succeeded);
                Check.That(attempts).IsEqualTo(2);
                Check.That(succeeded.Attempt).IsEqualTo(2);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Timeout_Without_Retry_Should_End_As_TimedOut() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();

                BackgroundJobHandle handle = server.GetBackgroundService().Enqueue(
                    "timeout-job",
                    ctx => Task.Delay(Timeout.InfiniteTimeSpan, ctx.CancellationToken),
                    options => {
                        options.Timeout = TimeSpan.FromMilliseconds(50);
                        options.RetryCount = 1;
                        options.RetryOnTimeout = false;
                    }
                );

                BackgroundJobSnapshot timedOut = await WaitForStatusAsync(server.GetBackgroundService(), handle.Id, BackgroundJobStatus.TimedOut);
                Check.That(timedOut.Attempt).IsEqualTo(1);
                Check.That(timedOut.Timeout).IsEqualTo(TimeSpan.FromMilliseconds(50));
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Timeout_Should_Remain_Cooperative_When_Work_Ignores_The_Token() {
            TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();

                BackgroundJobHandle handle = server.GetBackgroundService().Enqueue(
                    "uncooperative-timeout-job",
                    _ => release.Task,
                    options => {
                        options.Timeout = TimeSpan.FromMilliseconds(50);
                        options.RetryCount = 0;
                    }
                );

                await Task.Delay(150);
                BackgroundJobSnapshot? running = server.GetBackgroundService().GetJob(handle.Id);
                Check.That(running).IsNotNull();
                Check.That(running!.Status).IsEqualTo(BackgroundJobStatus.Running);

                release.TrySetResult(true);
                await WaitForStatusAsync(server.GetBackgroundService(), handle.Id, BackgroundJobStatus.TimedOut);
            }
            finally {
                release.TrySetResult(true);
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Cancel_Should_Cancel_Delayed_And_Running_Jobs() {
            bool delayedRan = false;
            TaskCompletionSource<bool> runningStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule();

            try {
                await server.StartAsync();
                IBackgroundService background = server.GetBackgroundService();

                BackgroundJobHandle delayed = background.EnqueueAfter("cancel-delayed", TimeSpan.FromMinutes(1), _ => {
                    delayedRan = true;
                    return Task.CompletedTask;
                });

                Check.That(background.Cancel(delayed.Id)).IsTrue();
                await WaitForStatusAsync(background, delayed.Id, BackgroundJobStatus.Canceled);
                Check.That(background.Cancel(delayed.Id)).IsFalse();
                Check.That(delayedRan).IsFalse();

                BackgroundJobHandle running = background.Enqueue("cancel-running", async ctx => {
                    runningStarted.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, ctx.CancellationToken);
                });

                Check.That(await runningStarted.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsTrue();
                Check.That(background.Cancel(running.Id)).IsTrue();
                await WaitForStatusAsync(background, running.Id, BackgroundJobStatus.Canceled);
                Check.That(background.Cancel(Guid.NewGuid())).IsFalse();
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task ScheduleEvery_Should_Start_After_Interval_And_Skip_Concurrent_Ticks() {
            int runs = 0;
            TimeSpan interval = TimeSpan.FromMilliseconds(100);
            TaskCompletionSource<DateTimeOffset> firstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseBackgroundModule(options => {
                options.ScheduleEvery(
                    "interval-job",
                    interval,
                    async ctx => {
                        Interlocked.Increment(ref runs);
                        firstRun.TrySetResult(DateTimeOffset.UtcNow);
                        await release.Task.WaitAsync(ctx.CancellationToken);
                    }
                );
            });

            try {
                DateTimeOffset startRequestedAtUtc = DateTimeOffset.UtcNow;
                await server.StartAsync();

                DateTimeOffset firstRunAtUtc = await firstRun.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Check.That(firstRunAtUtc >= startRequestedAtUtc.Add(interval)).IsTrue();

                await Task.Delay(250);
                Check.That(runs).IsEqualTo(1);
                Check.That(server.GetBackgroundService().GetJobs().Any(job => job.Name == "interval-job" && job.Source == "interval")).IsTrue();

                release.TrySetResult(true);
            }
            finally {
                release.TrySetResult(true);
                await server.StopAsync();
            }
        }

        private static async Task<BackgroundJobSnapshot> WaitForStatusAsync(IBackgroundService background, Guid jobId, BackgroundJobStatus status) {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

            while (!timeout.IsCancellationRequested) {
                BackgroundJobSnapshot? snapshot = background.GetJob(jobId);
                if (snapshot != null && snapshot.Status == status) {
                    return snapshot;
                }

                await Task.Delay(25, timeout.Token).ConfigureAwait(false);
            }

            throw new TimeoutException($"Job '{jobId}' did not reach status '{status}'.");
        }

        private static async Task WaitForMetricAsync(Func<bool> condition) {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));

            while (!timeout.IsCancellationRequested) {
                if (condition()) {
                    return;
                }

                await Task.Delay(25, timeout.Token).ConfigureAwait(false);
            }

            throw new TimeoutException("Telemetry metric was not observed.");
        }

        private static long CounterValue(ConcurrentDictionary<string, long> counters, string name) {
            return counters.TryGetValue(name, out long value) ? value : 0;
        }

        private sealed class RecordingBackgroundJobStore : IBackgroundJobStore {

            private readonly ConcurrentDictionary<Guid, BackgroundJobSnapshot> _jobs = new();
            private int _saveCount;

            public int SaveCount => Volatile.Read(ref _saveCount);

            public void Save(BackgroundJobSnapshot snapshot) {
                _jobs[snapshot.Id] = snapshot;
                Interlocked.Increment(ref _saveCount);
            }

            public bool TryGet(Guid id, out BackgroundJobSnapshot? snapshot) => _jobs.TryGetValue(id, out snapshot);

            public IReadOnlyCollection<BackgroundJobSnapshot> GetAll() => _jobs.Values.ToArray();

            public bool Remove(Guid id) => _jobs.TryRemove(id, out _);

        }

    }

}
