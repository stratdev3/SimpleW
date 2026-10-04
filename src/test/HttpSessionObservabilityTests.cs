using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using SimpleW;
using SimpleW.Observability;
using Xunit;

namespace test {

    [CollectionDefinition("HttpSession logging", DisableParallelization = true)]
    public sealed class HttpSessionLoggingCollection { }

    [Collection("HttpSession logging")]
    public sealed class HttpSessionObservabilityTests : IDisposable {
        private readonly Action<LogEntry>? _previousSink = Log.Sink;
        private readonly LogLevel _previousLevel = Log.MinimumLevel;
        private readonly List<(LogEntry Entry, long Timestamp)> _logs = new();
        private readonly List<Activity> _stoppedActivities = new();
        private readonly Dictionary<string, long> _counters = new();
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private readonly ActivityListener _activities;
        private readonly MeterListener _meters;

        public HttpSessionObservabilityTests() {
            Log.SetSink(entry => {
                if (entry.Message.Contains("session-")) {
                    _logs.Add((entry, Stopwatch.GetTimestamp()));
                }
            }, LogLevel.Information);
            _activities = new ActivityListener {
                ShouldListenTo = source => source.Name == $"SimpleW.{_instanceId}",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    SampleActivities ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
                ActivityStopped = activity => _stoppedActivities.Add(activity)
            };
            ActivitySource.AddActivityListener(_activities);
            _meters = new MeterListener {
                InstrumentPublished = (instrument, listener) => {
                    if (instrument.Meter.Name == $"SimpleW.{_instanceId}") {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _meters.SetMeasurementEventCallback<long>((instrument, value, _, _) => {
                _counters[instrument.Name] = _counters.GetValueOrDefault(instrument.Name) + value;
            });
            _meters.Start();
        }

        private bool SampleActivities { get; set; } = true;

        [Theory]
        [InlineData(false, false, true, false)]
        [InlineData(true, false, true, false)]
        [InlineData(false, true, true, false)]
        [InlineData(true, true, true, false)]
        [InlineData(false, true, false, false)]
        [InlineData(true, true, false, false)]
        [InlineData(false, true, true, true)]
        [InlineData(true, true, true, true)]
        public async Task Repeated_Requests_Should_Have_Independent_Timing(
            bool pipelined, bool telemetryEnabled, bool sampleActivities, bool disableResponseTelemetry) {
            SampleActivities = sampleActivities;
            var server = CreateServer(telemetryEnabled);
            int dispatches = 0;
            server.MapGet("/", async (HttpSession session) => {
                if (++dispatches == 1) {
                    await Task.Delay(120);
                }
                if (disableResponseTelemetry) {
                    session.Response.DisableTelemetry();
                }
                await session.Response.Text("OK").SendAsync();
                // Sending the response must not reset timing or stop the activity inside the handler.
                Assert.True(Field<bool>(session, "_requestTimingStarted"));
                Assert.Equal(telemetryEnabled && sampleActivities ? dispatches - 1 : 0, _stoppedActivities.Count);
            });

            long secondReadTimestamp = 0;
            string request = "GET / HTTP/1.1\r\nHost: localhost\r\n\r\n";
            using var transport = CreateTransport(
                pipelined ? new[] { request + request } : new[] { request, request },
                async index => {
                    if (!pipelined && index == 1) {
                        await Task.Delay(120);
                        secondReadTimestamp = Stopwatch.GetTimestamp();
                    }
                });
            await server.StartAsync();
            try {
                using var session = new HttpSession(server, transport, ArrayPool<byte>.Shared);
                await session.ProcessAsync();

                Assert.Equal(2, dispatches);
                Assert.Equal(2, _logs.Count);
                Assert.True(Duration(_logs[0].Entry) >= 100);
                long secondStart = pipelined ? _logs[0].Timestamp : secondReadTimestamp;
                Assert.InRange(Duration(_logs[1].Entry), 0,
                    Stopwatch.GetElapsedTime(secondStart, _logs[1].Timestamp).TotalMilliseconds + 5);
                AssertReset(session);
                int expectedMetrics = telemetryEnabled && sampleActivities && !disableResponseTelemetry ? 2 : 0;
                Assert.Equal(expectedMetrics, _counters.GetValueOrDefault("http.server.request.count"));
                Assert.Equal(expectedMetrics, _counters.GetValueOrDefault("http.server.response.count"));
                Assert.Equal(telemetryEnabled && sampleActivities ? 2 : 0, _stoppedActivities.Count);
                if (disableResponseTelemetry) {
                    Assert.All(_stoppedActivities, activity => {
                        Assert.False(activity.Recorded);
                        Assert.False(activity.IsAllDataRequested);
                    });
                }
                if (!telemetryEnabled) {
                    Assert.Null(server.Telemetry);
                    Assert.Empty(_counters);
                }
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Fragmented_Request_Should_Include_Time_Waiting_For_Remaining_Bytes() {
            var server = CreateServer(false);
            server.MapGet("/", () => "OK");
            long fragmentStart = 0;
            long fragmentEnd = 0;
            using var transport = CreateTransport(
                new[] { "GET / HTTP/1.1\r\nHost:", " localhost\r\n\r\n" },
                async index => {
                    if (index == 1) {
                        fragmentStart = Stopwatch.GetTimestamp();
                        await Task.Delay(120);
                        fragmentEnd = Stopwatch.GetTimestamp();
                    }
                });
            using var session = new HttpSession(server, transport, ArrayPool<byte>.Shared);
            await session.ProcessAsync();

            var log = Assert.Single(_logs);
            Assert.True(Duration(log.Entry) >= Stopwatch.GetElapsedTime(fragmentStart, fragmentEnd).TotalMilliseconds - 1);
            AssertReset(session);
        }

        [Theory]
        [InlineData("parse", 400)]
        [InlineData("fallback", 500)]
        [InlineData("throw", 500)]
        public async Task Error_Response_Should_Log_And_Reset_Without_Telemetry(string scenario, int status) {
            var server = CreateServer(false);
            server.MapGet("/", (HttpSession session) => {
                if (scenario == "throw") {
                    throw new InvalidOperationException("handler failure");
                }
                return Task.CompletedTask;
            });
            using var transport = CreateTransport(new[] {
                scenario == "parse" ? "GET / HTTP/1.1\r\nInvalidHeader\r\n\r\n" : "GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"
            });
            using var session = new HttpSession(server, transport, ArrayPool<byte>.Shared);
            long start = Stopwatch.GetTimestamp();
            await session.ProcessAsync();

            var log = Assert.Single(_logs);
            Assert.Equal(LogLevel.Error, log.Entry.Level);
            Assert.NotNull(log.Entry.Exception);
            Assert.Contains($" {status} ", log.Entry.Message);
            Assert.InRange(Duration(log.Entry), 0, Stopwatch.GetElapsedTime(start, log.Timestamp).TotalMilliseconds + 5);
            AssertReset(session);
            Assert.Empty(_stoppedActivities);
            Assert.Empty(_counters);
            Assert.Null(server.Telemetry);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Failed_Error_Response_Send_Should_Still_Reset_Observability(bool telemetryEnabled) {
            var server = CreateServer(telemetryEnabled);
            using var transport = CreateTransport(new[] { "GET / HTTP/1.1\r\nInvalidHeader\r\n\r\n" }, failWrite: true);
            await server.StartAsync();
            try {
                using var session = new HttpSession(server, transport, ArrayPool<byte>.Shared);
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.ProcessAsync());
                AssertReset(session);
                Assert.Equal(telemetryEnabled ? 1 : 0, _stoppedActivities.Count);
            }
            finally {
                await server.StopAsync();
            }
        }

        private SimpleWServer CreateServer(bool telemetryEnabled) {
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseEngine(new TestEngine());
            server.ConfigureTelemetry(options => {
                options.Enabled = telemetryEnabled;
                options.InstanceId = _instanceId;
            });
            return server;
        }

        private static DelegateTransport CreateTransport(string[] chunks, Func<int, Task>? beforeRead = null, bool failWrite = false) {
            int index = 0;
            return new DelegateTransport(
                async (destination, _) => {
                    if (index == chunks.Length) {
                        return 0;
                    }
                    if (beforeRead != null) {
                        await beforeRead(index);
                    }
                    byte[] bytes = Encoding.ASCII.GetBytes(chunks[index++]);
                    bytes.CopyTo(destination);
                    return bytes.Length;
                },
                (buffer, _) => failWrite ? throw new InvalidOperationException("write failed") : new ValueTask<long>(buffer.Length));
        }

        private static double Duration(LogEntry entry) =>
            double.Parse(Regex.Match(entry.Message, @" (\d+)ms ").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        private static T Field<T>(HttpSession session, string name) =>
            (T)typeof(HttpSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;

        private static void AssertReset(HttpSession session) {
            Assert.Equal(0, Field<long>(session, "_requestStartWatch"));
            Assert.Equal(0, Field<long>(session, "_responseStartWatch"));
            Assert.False(Field<bool>(session, "_requestTimingStarted"));
            Assert.Null(Field<Activity?>(session, "_currentActivity"));
        }

        public void Dispose() {
            _meters.Dispose();
            _activities.Dispose();
            Log.SetSink(_previousSink!, _previousLevel);
        }

        private sealed class TestEngine : ISimpleWEngine {
            public Task<EndPoint?> StartAsync(SimpleWServer server, ArrayPool<byte> bufferPool, CancellationToken cancellationToken = default)
                => Task.FromResult<EndPoint?>(server.EndPoint);
            public Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
