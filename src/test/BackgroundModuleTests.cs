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
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            try {
                Assert.Throws<InvalidOperationException>(() => server.GetBackgroundService());
            }
            finally {
                PortManager.ReleasePort(server.Port);
            }
        }

        [Fact]
        public void TryEnqueue_Should_Return_False_When_Queue_Is_Full() {
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
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
                PortManager.ReleasePort(server.Port);
            }
        }

        [Fact]
        public async Task Telemetry_Should_Record_Background_Counters() {
            int port = PortManager.GetFreePort();
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

            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.EnableTelemetry();
            server.UseBackgroundModule(options => {
                options.EnableTelemetry = true;
                options.Capacity = 1;
            });

            try {
                IBackgroundService background = server.GetBackgroundService();

                bool first = background.TryEnqueue("telemetry-job", ctx => {
                    ctx.ReportProgress(50, "half");
                    return Task.CompletedTask;
                }, out BackgroundJobHandle firstHandle);

                bool second = background.TryEnqueue("rejected-job", _ => Task.CompletedTask, out BackgroundJobHandle secondHandle);

                Check.That(first).IsTrue();
                Check.That(firstHandle.Id).IsNotEqualTo(Guid.Empty);
                Check.That(second).IsFalse();
                Check.That(secondHandle.Id).IsEqualTo(Guid.Empty);

                await server.StartAsync();
                await WaitForStatusAsync(background, firstHandle.Id, BackgroundJobStatus.Succeeded);
                await WaitForMetricAsync(() => CounterValue(counters, "simplew.background.job.completed.count") == 1);

                Check.That(CounterValue(counters, "simplew.background.job.enqueued.count")).IsEqualTo(1);
                Check.That(CounterValue(counters, "simplew.background.job.rejected.count")).IsEqualTo(1);
                Check.That(CounterValue(counters, "simplew.background.job.started.count")).IsEqualTo(1);
                Check.That(CounterValue(counters, "simplew.background.job.completed.count")).IsEqualTo(1);
                Check.That(CounterValue(counters, "simplew.background.job.progress.count")).IsEqualTo(1);
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Custom_JobStore_Should_Receive_Job_Snapshots() {
            int port = PortManager.GetFreePort();
            RecordingBackgroundJobStore store = new();

            var server = new SimpleWServer(IPAddress.Loopback, port);
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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Handler_Should_Return_Immediately_And_Run_Job_In_Background() {
            int port = PortManager.GetFreePort();
            TaskCompletionSource<bool> jobStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseJob = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<Guid> jobIdSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, port);
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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task ReportProgress_Should_Update_Job_Snapshot() {
            int port = PortManager.GetFreePort();
            TaskCompletionSource<bool> progressReported = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseJob = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<Guid> jobIdSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, port);
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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Failed_Job_Should_Keep_Failed_Snapshot() {
            int port = PortManager.GetFreePort();
            TaskCompletionSource<Guid> jobIdSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, port);
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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Cron_Schedule_Should_Enqueue_And_Run_Job() {
            int port = PortManager.GetFreePort();
            TaskCompletionSource<bool> cronRan = new(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, port);
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
                PortManager.ReleasePort(port);
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
