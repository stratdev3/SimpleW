using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.Helper.Jwt;
using SimpleW.Modules;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for SimpleWServer
    /// </summary>
    public class SimpleWServerTests {

        #region constructor

        [Fact]
        public void UseAddress_Should_Update_Endpoint_Before_Start() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);

            server.UseAddress(IPAddress.Any);

            Check.That(server.Address).IsEqualTo(IPAddress.Any);
            Check.That(server.Port).IsEqualTo(5001);
        }

        [Fact]
        public void UsePort_Should_Update_Endpoint_Before_Start() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);

            server.UsePort(5002);

            Check.That(server.Address).IsEqualTo(IPAddress.Loopback);
            Check.That(server.Port).IsEqualTo(5002);
        }

        [Fact]
        public async Task UseAddress_AfterStart_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.MapGet("/", () => new { ok = true });

            await server.StartAsync();

            Assert.Throws<InvalidOperationException>(() => server.UseAddress(IPAddress.Any));

            await server.StopAsync();
        }

        [Fact]
        public async Task UsePort_AfterStart_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.MapGet("/", () => new { ok = true });

            await server.StartAsync();

            Assert.Throws<InvalidOperationException>(() => server.UsePort(server.Port + 1));

            await server.StopAsync();
        }

        #endregion constructor

        #region controller action executor factory

        [Fact]
        public void UseControllerActionExecutorFactory_Should_Replace_Default_Factory_Before_Start() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);
            var factoryTracker = new FakeControllerActionExecutorFactory();
            ControllerActionExecutorFactory factory = factoryTracker.Create;

            server.UseControllerActionExecutorFactory(factory);

            Check.That(server.ControllerActionExecutorFactory).IsSameReferenceAs(factory);
        }

        [Fact]
        public async Task UseControllerActionExecutorFactory_AfterStart_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.MapGet("/", () => new { ok = true });

            await server.StartAsync();

            Assert.Throws<InvalidOperationException>(() => server.UseControllerActionExecutorFactory((_, _) => static (session, resultHandler) => resultHandler(session, new { ok = true })));

            await server.StopAsync();
        }

        [Fact]
        public async Task MapController_Should_Use_Custom_ControllerActionExecutorFactory() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            var factoryTracker = new FakeControllerActionExecutorFactory();

            server.UseControllerActionExecutorFactory(factoryTracker.Create);
            server.MapController<FakeActionFactoryController>("/api");

            try {
                await server.StartAsync();

                var client = new HttpClient();
                var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/factory/ping");
                var content = await response.Content.ReadAsStringAsync();

                Check.That(factoryTracker.CallCount).IsEqualTo(1);
                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { ok = true, mode = "custom-executor-factory" }));
            }
            finally {
                await server.StopAsync();
            }
        }

        private sealed class FakeControllerActionExecutorFactory {

            public int CallCount { get; private set; }

            public HttpRouteExecutor Create(Type controllerType, MethodInfo method) {
                CallCount++;
                return static (session, resultHandler) => resultHandler(session, new { ok = true, mode = "custom-executor-factory" });
            }

        }

        [Route("/factory")]
        private sealed class FakeActionFactoryController : Controller {

            [Route("GET", "/ping")]
            public object Ping() {
                return new { ok = false };
            }

        }

        #endregion controller action executor factory

        #region engine

        [Fact]
        public void Engine_Should_Default_To_SimpleWEngine() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);

            Check.That(server.Engine).IsInstanceOf<SimpleWEngine>();
        }

        [Fact]
        public void ConfigureEngine_Should_Replace_Default_Engine_Before_Start() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);
            var engine = new FakeEngine();

            server.UseEngine(engine);

            Check.That(server.Engine).IsSameReferenceAs(engine);
        }

        [Fact]
        public async Task ConfigureEngine_AfterStart_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.MapGet("/", () => new { ok = true });

            await server.StartAsync();

            Assert.Throws<InvalidOperationException>(() => server.UseEngine(new FakeEngine()));

            await server.StopAsync();
        }

        [Fact]
        public async Task CustomEngine_Should_Be_Used_For_Start_And_Stop() {

            var engine = new FakeEngine();
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseEngine(engine);

            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            await server.StartAsync();
            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            await server.StopAsync();

            Check.That(engine.StartCallCount).IsEqualTo(1);
            Check.That(engine.StopCallCount).IsEqualTo(1);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);

        }

        [Fact]
        public async Task Concurrent_Starts_Should_Start_Engine_Once() {

            var startEntered = NewSignal();
            var releaseStart = NewSignal();
            var engine = new ControllableEngine {
                StartHandler = async (_, server, _) => {
                    startEntered.TrySetResult(true);
                    await releaseStart.Task;
                    return server.EndPoint;
                }
            };
            var server = CreateServer(engine);

            Task firstStart = server.StartAsync();
            await startEntered.Task;
            Check.That(server.State).IsEqualTo(SimpleWServerState.Starting);

            Task secondStart = server.StartAsync();
            Check.That(secondStart.IsCompleted).IsFalse();

            releaseStart.TrySetResult(true);
            await Task.WhenAll(firstStart, secondStart);

            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Check.That(engine.StartCallCount).IsEqualTo(1);
            await server.StopAsync();
        }

        [Fact]
        public async Task Stop_During_Start_Should_Wait_Then_Stop() {

            var startEntered = NewSignal();
            var releaseStart = NewSignal();
            var engine = new ControllableEngine {
                StartHandler = async (_, server, _) => {
                    startEntered.TrySetResult(true);
                    await releaseStart.Task;
                    return server.EndPoint;
                }
            };
            var server = CreateServer(engine);

            Task start = server.StartAsync();
            await startEntered.Task;
            Task stop = server.StopAsync();

            Check.That(stop.IsCompleted).IsFalse();
            releaseStart.TrySetResult(true);
            await Task.WhenAll(start, stop);

            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            Check.That(engine.StartCallCount).IsEqualTo(1);
            Check.That(engine.StopCallCount).IsEqualTo(1);
        }

        [Fact]
        public async Task Start_During_Stop_Should_Wait_Then_Restart() {

            var stopEntered = NewSignal();
            var releaseStop = NewSignal();
            var engine = new ControllableEngine {
                StopHandler = async (call, _, _) => {
                    if (call == 1) {
                        stopEntered.TrySetResult(true);
                        await releaseStop.Task;
                    }
                }
            };
            var server = CreateServer(engine);
            await server.StartAsync();

            Task stop = server.StopAsync();
            await stopEntered.Task;
            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopping);

            Task start = server.StartAsync();
            Check.That(start.IsCompleted).IsFalse();
            releaseStop.TrySetResult(true);
            await Task.WhenAll(stop, start);

            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Check.That(engine.StartCallCount).IsEqualTo(2);
            Check.That(engine.StopCallCount).IsEqualTo(1);
            await server.StopAsync();
        }

        [Fact]
        public async Task Concurrent_Stops_Should_Stop_Engine_Once() {

            var stopEntered = NewSignal();
            var releaseStop = NewSignal();
            var engine = new ControllableEngine {
                StopHandler = async (_, _, _) => {
                    stopEntered.TrySetResult(true);
                    await releaseStop.Task;
                }
            };
            var server = CreateServer(engine);
            await server.StartAsync();

            Task firstStop = server.StopAsync();
            await stopEntered.Task;
            Task secondStop = server.StopAsync();

            Check.That(secondStop.IsCompleted).IsFalse();
            releaseStop.TrySetResult(true);
            await Task.WhenAll(firstStop, secondStop);

            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            Check.That(engine.StopCallCount).IsEqualTo(1);
        }

        [Fact]
        public async Task Reload_During_Start_Should_Wait_Then_Reload() {

            var startEntered = NewSignal();
            var releaseStart = NewSignal();
            var engine = new ControllableEngine {
                StartHandler = async (call, server, _) => {
                    if (call == 1) {
                        startEntered.TrySetResult(true);
                        await releaseStart.Task;
                    }
                    return server.EndPoint;
                }
            };
            var server = CreateServer(engine);

            Task start = server.StartAsync();
            await startEntered.Task;
            Task reload = server.ReloadListenerAsync(_ => { });

            Check.That(reload.IsCompleted).IsFalse();
            releaseStart.TrySetResult(true);
            await Task.WhenAll(start, reload);

            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Check.That(engine.StartCallCount).IsEqualTo(2);
            Check.That(engine.StopCallCount).IsEqualTo(1);
            await server.StopAsync();
        }

        [Fact]
        public async Task Stop_During_Reload_Should_Wait_Then_Stop() {

            var reloadStartEntered = NewSignal();
            var releaseReloadStart = NewSignal();
            var engine = new ControllableEngine {
                StartHandler = async (call, server, _) => {
                    if (call == 2) {
                        reloadStartEntered.TrySetResult(true);
                        await releaseReloadStart.Task;
                    }
                    return server.EndPoint;
                }
            };
            var server = CreateServer(engine);
            await server.StartAsync();

            Task reload = server.ReloadListenerAsync(_ => { });
            await reloadStartEntered.Task;
            Check.That(server.State).IsEqualTo(SimpleWServerState.Reloading);

            Task stop = server.StopAsync();
            Check.That(stop.IsCompleted).IsFalse();
            releaseReloadStart.TrySetResult(true);
            await Task.WhenAll(reload, stop);

            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            Check.That(engine.StartCallCount).IsEqualTo(2);
            Check.That(engine.StopCallCount).IsEqualTo(2);
        }

        [Fact]
        public async Task Start_Failure_Should_Return_To_Stopped_Without_Stopping_Engine() {

            var startException = new InvalidOperationException("start failed");
            var engine = new ControllableEngine {
                StartHandler = (_, _, _) => Task.FromException<EndPoint?>(startException)
            };
            var server = CreateServer(engine);
            var states = new List<SimpleWServerState>();
            server.OnStateChanged((_, state) => states.Add(state));

            Exception exception = await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());

            Assert.Same(startException, exception);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            Check.That(engine.StopCallCount).IsEqualTo(0);
            Assert.Equal(new[] { SimpleWServerState.Starting, SimpleWServerState.Stopped }, states);
        }

        [Fact]
        public async Task Start_And_Cleanup_Failure_Should_Fault_Until_Stop_Recovers() {

            var postStartException = new InvalidOperationException("post-start failed");
            var cleanupException = new InvalidOperationException("cleanup failed");
            int isEncryptedReadCount = 0;
            var engine = new ControllableEngine {
                StartHandler = (_, server, _) => Task.FromResult<EndPoint?>(server.EndPoint),
                IsEncryptedHandler = () => Interlocked.Increment(ref isEncryptedReadCount) == 1
                    ? throw postStartException
                    : false,
                StopHandler = (call, _, _) => call == 1
                    ? Task.FromException(cleanupException)
                    : Task.CompletedTask
            };
            var server = CreateServer(engine);
            var states = new List<SimpleWServerState>();
            server.OnStateChanged((_, state) => states.Add(state));

            AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() => server.StartAsync());

            Assert.Contains(postStartException, exception.InnerExceptions);
            Assert.Contains(cleanupException, exception.InnerExceptions);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Faulted);
            Assert.Equal(new[] { SimpleWServerState.Starting, SimpleWServerState.Faulted }, states);
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
            Assert.Throws<InvalidOperationException>(() => server.Configure(_ => { }));
            var faultedModule = new TrackingModule();
            Assert.Throws<InvalidOperationException>(() => server.UseModule(faultedModule));
            Check.That(faultedModule.InstallCallCount).IsEqualTo(0);

            states.Clear();
            await server.StopAsync();
            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            await server.StartAsync();
            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Assert.Equal(new[] {
                SimpleWServerState.Stopping,
                SimpleWServerState.Stopped,
                SimpleWServerState.Starting,
                SimpleWServerState.Started
            }, states);
            await server.StopAsync();
        }

        [Fact]
        public async Task Stop_Failure_Should_Fault_And_A_Subsequent_Stop_Should_Recover() {

            var stopException = new InvalidOperationException("stop failed");
            var engine = new ControllableEngine {
                StopHandler = (call, _, _) => call == 1
                    ? Task.FromException(stopException)
                    : Task.CompletedTask
            };
            var server = CreateServer(engine);
            var states = new List<SimpleWServerState>();
            server.OnStateChanged((_, state) => states.Add(state));
            await server.StartAsync();
            states.Clear();

            Exception exception = await Assert.ThrowsAsync<InvalidOperationException>(() => server.StopAsync());

            Assert.Same(stopException, exception);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Faulted);
            Assert.Equal(new[] { SimpleWServerState.Stopping, SimpleWServerState.Faulted }, states);
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());

            states.Clear();
            await server.StopAsync();
            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            Assert.Equal(new[] { SimpleWServerState.Stopping, SimpleWServerState.Stopped }, states);
            await server.StartAsync();
            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            await server.StopAsync();
        }

        [Fact]
        public async Task Reload_Success_Should_Return_To_Started() {

            var engine = new ControllableEngine();
            var server = CreateServer(engine);
            await server.StartAsync();
            var states = new List<SimpleWServerState>();
            server.OnStateChanged((_, state) => states.Add(state));

            await server.ReloadListenerAsync(s => s.UsePort(5002));

            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Check.That(server.Port).IsEqualTo(5002);
            Check.That(engine.StartCallCount).IsEqualTo(2);
            Check.That(engine.StopCallCount).IsEqualTo(1);
            Assert.Equal(new[] { SimpleWServerState.Reloading, SimpleWServerState.Started }, states);
            await server.StopAsync();
        }

        [Fact]
        public async Task Reload_Failure_With_Successful_Rollback_Should_Restore_Listener_And_State() {

            var reloadException = new InvalidOperationException("reload failed");
            var engine = new ControllableEngine {
                StartHandler = (call, server, _) => call == 2
                    ? Task.FromException<EndPoint?>(reloadException)
                    : Task.FromResult<EndPoint?>(server.EndPoint)
            };
            var server = CreateServer(engine);
            await server.StartAsync();
            EndPoint oldEndPoint = server.EndPoint;
            var states = new List<SimpleWServerState>();
            server.OnStateChanged((_, state) => states.Add(state));

            ListenerReloadException exception = await Assert.ThrowsAsync<ListenerReloadException>(
                () => server.ReloadListenerAsync(s => s.UsePort(5002))
            );

            Assert.True(exception.ListenerRestored);
            Assert.Same(reloadException, exception.ReloadException);
            Assert.Null(exception.RollbackException);
            Assert.Same(oldEndPoint, server.EndPoint);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Assert.Equal(new[] { SimpleWServerState.Reloading, SimpleWServerState.Started }, states);
            await server.StopAsync();
        }

        [Fact]
        public async Task Reload_Rollback_Should_Not_Use_The_Cancelled_Reload_Token() {

            using var reloadCancellation = new CancellationTokenSource();
            bool rollbackStopUsedNonCancelledToken = false;
            bool rollbackStartUsedNonCancelledToken = false;
            var engine = new ControllableEngine {
                StartHandler = (call, server, token) => {
                    if (call == 2) {
                        token.ThrowIfCancellationRequested();
                    }
                    if (call == 3) {
                        rollbackStartUsedNonCancelledToken = !token.CanBeCanceled;
                    }
                    return Task.FromResult<EndPoint?>(server.EndPoint);
                },
                StopHandler = (call, _, token) => {
                    if (call == 2) {
                        rollbackStopUsedNonCancelledToken = !token.CanBeCanceled;
                    }
                    return Task.CompletedTask;
                }
            };
            var server = CreateServer(engine);
            await server.StartAsync();

            ListenerReloadException exception = await Assert.ThrowsAsync<ListenerReloadException>(
                () => server.ReloadListenerAsync(_ => reloadCancellation.Cancel(), reloadCancellation.Token)
            );

            Assert.True(exception.ListenerRestored);
            Assert.True(rollbackStopUsedNonCancelledToken);
            Assert.True(rollbackStartUsedNonCancelledToken);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            await server.StopAsync();
        }

        [Fact]
        public async Task Reload_And_Rollback_Failure_Should_Fault_With_Both_Errors() {

            var reloadException = new InvalidOperationException("reload failed");
            var rollbackException = new InvalidOperationException("rollback failed");
            var engine = new ControllableEngine {
                StartHandler = (call, server, _) => call == 2
                    ? Task.FromException<EndPoint?>(reloadException)
                    : Task.FromResult<EndPoint?>(server.EndPoint),
                StopHandler = (call, _, _) => call == 2
                    ? Task.FromException(rollbackException)
                    : Task.CompletedTask
            };
            var server = CreateServer(engine);
            await server.StartAsync();
            var states = new List<SimpleWServerState>();
            server.OnStateChanged((_, state) => states.Add(state));

            ListenerReloadException exception = await Assert.ThrowsAsync<ListenerReloadException>(
                () => server.ReloadListenerAsync(s => s.UsePort(5002))
            );

            Assert.False(exception.ListenerRestored);
            Assert.Same(reloadException, exception.ReloadException);
            Assert.Same(rollbackException, exception.RollbackException);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Faulted);
            Assert.Equal(new[] { SimpleWServerState.Reloading, SimpleWServerState.Faulted }, states);
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.ReloadListenerAsync(_ => { }));

            await server.StopAsync();
            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
        }

        [Fact]
        public async Task Cancellation_From_An_Old_Generation_Should_Not_Stop_A_Restarted_Server() {

            using var firstLifetime = new CancellationTokenSource();
            using var secondLifetime = new CancellationTokenSource();
            var engine = new ControllableEngine();
            var server = CreateServer(engine);

            await server.StartAsync(firstLifetime.Token);
            await server.StopAsync();
            await server.StartAsync(secondLifetime.Token);

            firstLifetime.Cancel();
            await Task.Yield();

            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Check.That(engine.StopCallCount).IsEqualTo(1);
            await server.StopAsync();
        }

        [Fact]
        public async Task RunAsync_Should_Wait_For_The_Generation_It_Started() {

            using var lifetime = new CancellationTokenSource();
            var startEntered = NewSignal();
            var engine = new ControllableEngine {
                StartHandler = (_, server, _) => {
                    startEntered.TrySetResult(true);
                    return Task.FromResult<EndPoint?>(server.EndPoint);
                }
            };
            var server = CreateServer(engine);

            Task run = server.RunAsync(lifetime.Token);
            await startEntered.Task;

            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Check.That(run.IsCompleted).IsFalse();

            lifetime.Cancel();
            await run;

            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            Check.That(engine.StopCallCount).IsEqualTo(1);
        }

        [Fact]
        public async Task Configuration_And_Module_Installation_Should_Require_Stopped() {

            var startEntered = NewSignal();
            var releaseStart = NewSignal();
            var reloadEntered = NewSignal();
            var releaseReload = NewSignal();
            var stopEntered = NewSignal();
            var releaseStop = NewSignal();
            var engine = new ControllableEngine {
                StartHandler = async (call, server, _) => {
                    if (call == 1) {
                        startEntered.TrySetResult(true);
                        await releaseStart.Task;
                    }
                    else if (call == 2) {
                        reloadEntered.TrySetResult(true);
                        await releaseReload.Task;
                    }
                    return server.EndPoint;
                },
                StopHandler = async (call, _, _) => {
                    if (call == 2) {
                        stopEntered.TrySetResult(true);
                        await releaseStop.Task;
                    }
                }
            };
            var server = CreateServer(engine);
            var module = new TrackingModule();

            server.Configure(_ => { });
            server.UseModule(module);
            Check.That(module.InstallCallCount).IsEqualTo(1);

            Task start = server.StartAsync();
            await startEntered.Task;
            Assert.Throws<InvalidOperationException>(() => server.Configure(_ => { }));
            Assert.Throws<InvalidOperationException>(() => server.UseModule(module));
            releaseStart.TrySetResult(true);
            await start;

            Assert.Throws<InvalidOperationException>(() => server.Configure(_ => { }));
            Assert.Throws<InvalidOperationException>(() => server.UseModule(module));

            Task reload = server.ReloadListenerAsync(_ => { });
            await reloadEntered.Task;
            Assert.Throws<InvalidOperationException>(() => server.Configure(_ => { }));
            Assert.Throws<InvalidOperationException>(() => server.UseModule(module));
            server.UsePort(5002);
            releaseReload.TrySetResult(true);
            await reload;

            Task stop = server.StopAsync();
            await stopEntered.Task;
            Assert.Throws<InvalidOperationException>(() => server.Configure(_ => { }));
            Assert.Throws<InvalidOperationException>(() => server.UseModule(module));
            releaseStop.TrySetResult(true);
            await stop;

            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
            Check.That(module.InstallCallCount).IsEqualTo(1);
        }

        [Fact]
        public async Task SimpleWEngine_Should_Not_Be_Shared_By_Two_Started_Servers() {

            var engine = new SimpleWEngine();
            var server1 = new SimpleWServer(IPAddress.Loopback, 0);
            var server2 = new SimpleWServer(IPAddress.Loopback, 0);

            server1.UseEngine(engine);
            server2.UseEngine(engine);
            server1.MapGet("/", () => new { ok = true });
            server2.MapGet("/", () => new { ok = true });

            try {
                await server1.StartAsync();

                await Assert.ThrowsAsync<InvalidOperationException>(() => server2.StartAsync());
                Check.That(server1.State).IsEqualTo(SimpleWServerState.Started);
                Check.That(server2.State).IsEqualTo(SimpleWServerState.Stopped);
            }
            finally {
                await server1.StopAsync();
                await server2.StopAsync();
            }
        }

        [Fact]
        public void UseEngine_WithOptions_Should_Configure_Default_Engine() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);

            server.UseEngine(options => {
                options.TcpNoDelay = true;
                options.ReuseAddress = true;
            });

            Check.That(server.Engine).IsInstanceOf<SimpleWEngine>();
        }

        [Fact]
        public async Task UseEngine_WithOptions_AfterStart_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.MapGet("/", () => new { ok = true });

            await server.StartAsync();

            Assert.Throws<InvalidOperationException>(() => server.UseEngine(options => {
                options.TcpNoDelay = true;
            }));

            await server.StopAsync();
        }

        [Fact]
        public void SimpleWEngineOptions_Should_Validate_Positive_ListenBacklog() {

            var options = new SimpleWEngineOptions { ListenBacklog = 0 };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndNormalize());
        }

        [Fact]
        public void SimpleWEngineOptions_Should_Validate_Positive_ReceiveBufferSize() {

            var options = new SimpleWEngineOptions { ReceiveBufferSize = 0 };

            Assert.Throws<ArgumentOutOfRangeException>(() => options.ValidateAndNormalize());
        }

        [Fact]
        public void SimpleWEngineOptions_Should_Reject_ReuseAddress_With_ExclusiveAddressUse() {

            var options = new SimpleWEngineOptions {
                ReuseAddress = true,
                ExclusiveAddressUse = true,
            };

            Assert.Throws<ArgumentException>(() => options.ValidateAndNormalize());
        }

        [Fact]
        public void SimpleWEngineOptions_ReusePort_Should_Require_Linux() {

            var options = new SimpleWEngineOptions {
                ReusePort = true,
                AcceptPerCore = true,
            };

            if (OperatingSystem.IsLinux()) {
                options.ValidateAndNormalize();
            }
            else {
                Assert.Throws<PlatformNotSupportedException>(() => options.ValidateAndNormalize());
            }
        }

        [Fact]
        public void SimpleWEngineOptions_ReusePort_Should_Require_AcceptPerCore_On_Linux() {

            var options = new SimpleWEngineOptions { ReusePort = true };

            if (OperatingSystem.IsLinux()) {
                Assert.Throws<ArgumentException>(() => options.ValidateAndNormalize());
            }
            else {
                Assert.Throws<PlatformNotSupportedException>(() => options.ValidateAndNormalize());
            }
        }

        private sealed class FakeEngine : ISimpleWEngine {

            public string Name => nameof(FakeEngine);

            public int StartCallCount { get; private set; }

            public int StopCallCount { get; private set; }

            public Task<EndPoint?> StartAsync(SimpleWServer server, ArrayPool<byte> bufferPool, CancellationToken cancellationToken = default) {
                StartCallCount++;
                return Task.FromResult<EndPoint?>(server.EndPoint);
            }

            public Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default) {
                StopCallCount++;
                return Task.CompletedTask;
            }

        }

        private sealed class ControllableEngine : ISimpleWEngine {

            private int _startCallCount;
            private int _stopCallCount;

            public string Name => nameof(ControllableEngine);

            public int StartCallCount => Volatile.Read(ref _startCallCount);

            public int StopCallCount => Volatile.Read(ref _stopCallCount);

            public Func<int, SimpleWServer, CancellationToken, Task<EndPoint?>>? StartHandler { get; init; }

            public Func<int, SimpleWServer, CancellationToken, Task>? StopHandler { get; init; }

            public Func<bool>? IsEncryptedHandler { get; init; }

            public bool IsEncrypted => IsEncryptedHandler?.Invoke() ?? false;

            public async Task<EndPoint?> StartAsync(SimpleWServer server, ArrayPool<byte> bufferPool, CancellationToken cancellationToken = default) {
                int call = Interlocked.Increment(ref _startCallCount);
                if (StartHandler != null) {
                    return await StartHandler(call, server, cancellationToken);
                }
                return server.EndPoint;
            }

            public async Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default) {
                int call = Interlocked.Increment(ref _stopCallCount);
                if (StopHandler != null) {
                    await StopHandler(call, server, cancellationToken);
                }
            }

        }

        private sealed class TrackingModule : IHttpModule {

            public int InstallCallCount { get; private set; }

            public void Install(SimpleWServer server) {
                InstallCallCount++;
            }

        }

        private static SimpleWServer CreateServer(ISimpleWEngine engine) {
            return new SimpleWServer(IPAddress.Loopback, 5001).UseEngine(engine);
        }

        private static TaskCompletionSource<bool> NewSignal() {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }


        #endregion engine

        #region result handler

        [Fact]
        public async Task ConfigureResultHandler_Should_Override_Default_Handler() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureResultHandler((session, result) => {
                return session.Response
                              .AddHeader("X-Result-Handler", "custom")
                              .Json(result)
                              .SendAsync();
            });

            server.MapGet("/api/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/hello");
            var content = await response.Content.ReadAsStringAsync();

            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Headers.Contains("X-Result-Handler")).IsTrue();
            Check.That(response.Headers.GetValues("X-Result-Handler").First()).IsEqualTo("custom");

            await server.StopAsync();
        }

        #endregion result handler

        #region MaxRequestSize

        [Fact]
        public async Task Should_Reject_Request_When_Header_Too_Large() {

            // arrange
            var server = new SimpleWServer(IPAddress.Loopback, 0)
                .Configure(o => {
                    o.MaxRequestHeaderSize = 64; // ultra small
                });

            server.Router.MapGet("/", () => "OK");

            await server.StartAsync();

            var endpoint = (IPEndPoint)server.EndPoint;

            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);

            using var stream = client.GetStream();

            // header too big
            string request =
                "GET / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "X-Big: " + new string('A', 200) + "\r\n" +
                "\r\n";

            var bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes);

            // read response
            var buffer = new byte[1024];
            int read = await stream.ReadAsync(buffer);

            // close or error
            Assert.True(read == 0 || Encoding.ASCII.GetString(buffer, 0, read).Contains("431"));

            await server.StopAsync();
        }

        [Fact]
        public async Task Should_Reject_Request_When_Body_Too_Large() {

            // arrange
            var server = new SimpleWServer(IPAddress.Loopback, 0)
                .Configure(o => {
                    o.MaxRequestBodySize = 32; // ultra small
                });

            server.Router.MapPost("/", (HttpSession s) => "OK");

            await server.StartAsync();

            var endpoint = (IPEndPoint)server.EndPoint;

            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);

            using var stream = client.GetStream();

            string body = new string('A', 200); // too big

            string request =
                "POST / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "\r\n" +
                body;

            var bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes);

            var buffer = new byte[1024];
            int read = await stream.ReadAsync(buffer);

            // close or erreur
            Assert.True(read == 0 || Encoding.ASCII.GetString(buffer, 0, read).Contains("413"));

            await server.StopAsync();
        }

        #endregion

        #region SessionTimeout

        [Fact]
        public async Task Should_Close_Connection_When_SessionTimeout_Reached() {

            // arrange
            var server = new SimpleWServer(IPAddress.Loopback, 0)
                .Configure(o => {
                    o.SessionTimeout = TimeSpan.FromMilliseconds(200); // très court
                });

            await server.StartAsync();

            var endpoint = (IPEndPoint)server.EndPoint;

            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);

            using var stream = client.GetStream();

            // on attend que le timeout serveur déclenche
            await Task.Delay(500);

            // try read → doit être fermé
            var buffer = new byte[1];
            int read = 0;

            try {
                read = await stream.ReadAsync(buffer);
            }
            catch {
                // socket déjà fermée → OK
            }

            // assert : soit 0 (closed), soit exception
            Assert.True(read == 0);

            await server.StopAsync();
        }

        #endregion SessionTimeout

        #region callbacks

        [Fact]
        public async Task OnStateChanged_Should_Notify_Each_Real_Transition() {

            var states = new List<SimpleWServerState>();
            var server = CreateServer(new ControllableEngine());
            server.OnStateChanged((_, state) => states.Add(state));

            Assert.Empty(states);
            await server.StartAsync();
            await server.StartAsync();
            await server.ReloadListenerAsync(_ => { });
            await server.StopAsync();
            await server.StopAsync();

            Assert.Equal(new[] {
                SimpleWServerState.Starting,
                SimpleWServerState.Started,
                SimpleWServerState.Reloading,
                SimpleWServerState.Started,
                SimpleWServerState.Stopping,
                SimpleWServerState.Stopped
            }, states);
        }

        [Fact]
        public async Task OnStateChanged_Should_Invoke_Sync_And_Async_Callbacks_In_Registration_Order() {

            var calls = new List<string>();
            var server = CreateServer(new ControllableEngine());

            server.OnStateChanged((s, state) => {
                Check.That(s.State).IsEqualTo(state);
                calls.Add($"sync-1:{state}");
            });
            server.OnStateChanged(async (s, state) => {
                await Task.Yield();
                Check.That(s.State).IsEqualTo(state);
                calls.Add($"async:{state}");
            });
            server.OnStateChanged((s, state) => {
                Check.That(s.State).IsEqualTo(state);
                calls.Add($"sync-2:{state}");
            });

            await server.StartAsync();

            Assert.Equal(new[] {
                "sync-1:Starting",
                "async:Starting",
                "sync-2:Starting",
                "sync-1:Started",
                "async:Started",
                "sync-2:Started"
            }, calls);

            await server.StopAsync();
        }

        [Fact]
        public async Task OnStateChanged_Should_Await_Async_Callbacks() {

            var callbackEntered = NewSignal();
            var releaseCallback = NewSignal();
            var server = CreateServer(new ControllableEngine());

            server.OnStateChanged(async (_, state) => {
                if (state == SimpleWServerState.Started) {
                    callbackEntered.TrySetResult(true);
                    await releaseCallback.Task;
                }
            });

            Task start = server.StartAsync();
            await callbackEntered.Task;

            Check.That(start.IsCompleted).IsFalse();
            releaseCallback.TrySetResult(true);
            await start;

            await server.StopAsync();
        }

        [Fact]
        public async Task OnStateChanged_Should_Isolate_Callback_Exceptions() {

            int followingCallbackCount = 0;
            var server = CreateServer(new ControllableEngine());

            server.OnStateChanged((_, state) => {
                if (state == SimpleWServerState.Started) {
                    throw new InvalidOperationException("callback failed");
                }
            });
            server.OnStateChanged((_, state) => {
                if (state == SimpleWServerState.Started) {
                    Interlocked.Increment(ref followingCallbackCount);
                }
            });

            await server.StartAsync();

            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);
            Check.That(followingCallbackCount).IsEqualTo(1);

            await server.StopAsync();
        }

        [Fact]
        public async Task State_Changed_Callbacks_Should_Reject_Reentrant_Lifecycle_Calls() {

            Task? stopFromStarted = null;
            Task? startFromStopped = null;
            var server = CreateServer(new ControllableEngine());

            server.OnStateChanged((s, state) => {
                if (state == SimpleWServerState.Started) {
                    stopFromStarted = s.StopAsync();
                }
                else if (state == SimpleWServerState.Stopped) {
                    startFromStopped = s.StartAsync();
                }
            });

            await server.StartAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => stopFromStarted!);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Started);

            await server.StopAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => startFromStopped!);
            Check.That(server.State).IsEqualTo(SimpleWServerState.Stopped);
        }

        #endregion callbacks

        #region client ip resolver

        [Fact]
        public async Task ConfigureClientIPResolver_Should_Override_ClientIpAddress() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));

            server.MapGet("/api/ip", (HttpSession session) => {
                return new {
                    ip = session.ClientIpAddress!.ToString()
                };
            });

            await server.StartAsync();

            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/ip");
            var content = await response.Content.ReadAsStringAsync();

            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new {
                ip = "203.0.113.10"
            }));

            await server.StopAsync();
        }

        #endregion client ip resolver

        #region slow request protection

        [Fact]
        public async Task RequestHeadersTimeout_Should_Close_Connection_And_May_Return_408_When_Headers_Are_Too_Slow() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.Configure(options => {
                options.RequestHeadersTimeout = TimeSpan.FromMilliseconds(150);
                options.SessionTimeout = TimeSpan.FromSeconds(5);
            });

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            try {
                // client
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, server.Port);

                using NetworkStream stream = client.GetStream();

                byte[] partialRequest = Encoding.ASCII.GetBytes(
                    "GET /api/test/hello HTTP/1.1\r\n" +
                    "Host: localhost\r\n"
                );

                await stream.WriteAsync(partialRequest);
                await stream.FlushAsync();

                // on laisse volontairement expirer le timeout headers
                await Task.Delay(350);

                byte[] buffer = new byte[4096];
                int read = await stream.ReadAsync(buffer);

                string response = read > 0
                    ? Encoding.ASCII.GetString(buffer, 0, read)
                    : string.Empty;

                // asserts
                if (read == 0) {
                    // connexion fermée sans qu'on récupère la réponse 408
                    Check.That(read).IsEqualTo(0);
                }
                else {
                    // on a récupéré la réponse timeout
                    Check.That(response).Contains("408 Request Timeout");
                    Check.That(response).Contains("Request headers timeout.");
                    Check.That(response).Contains("Connection: close");
                }
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task MinRequestBodyDataRateBytesPerSecond_Should_Return_408_When_Body_Is_Too_Slow_After_GracePeriod() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.Configure(options => {
                options.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
                options.MinRequestBodyDataRateBytesPerSecond = 20;
                options.RequestBodyGracePeriod = TimeSpan.FromMilliseconds(150);
                options.SessionTimeout = TimeSpan.FromSeconds(5);
            });

            server.MapPost("/api/test/slow-body", (HttpSession session) => {
                return session.Response.Text("OK");
            });

            await server.StartAsync();

            // client
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.Port);

            using NetworkStream stream = client.GetStream();

            string headers =
                "POST /api/test/slow-body HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Content-Length: 10\r\n" +
                "\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
            await stream.FlushAsync();

            // we let the grace period
            await Task.Delay(350);

            // send only one byte, under the min rate
            await stream.WriteAsync(new byte[] { (byte)'A' });
            await stream.FlushAsync();

            byte[] buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer);

            string response = Encoding.ASCII.GetString(buffer, 0, read);

            // asserts
            Check.That(read).IsStrictlyGreaterThan(0);
            Check.That(response).Contains("408 Request Timeout");
            Check.That(response).Contains("Request body too slow.");
            Check.That(response).Contains("Connection: close");

            // dispose
            await server.StopAsync();
        }

        [Fact]
        public async Task RequestBodyGracePeriod_Should_Allow_Slow_Start_If_Body_Finishes_Before_Rate_Check_Fails() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.Configure(options => {
                options.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
                options.MinRequestBodyDataRateBytesPerSecond = 100;
                options.RequestBodyGracePeriod = TimeSpan.FromMilliseconds(400);
                options.SessionTimeout = TimeSpan.FromSeconds(5);
            });

            server.MapPost("/api/test/grace", (HttpSession session) => {
                return session.Response.Text(session.Request.BodyString);
            });

            await server.StartAsync();

            // client
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.Port);

            using NetworkStream stream = client.GetStream();

            string body = "hello";
            string headers =
                "POST /api/test/grace HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
            await stream.FlushAsync();

            // start slow, but still in the grace period
            await Task.Delay(150);

            // then send all the body before simplew check
            await stream.WriteAsync(Encoding.ASCII.GetBytes(body));
            await stream.FlushAsync();

            byte[] buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer);

            string response = Encoding.ASCII.GetString(buffer, 0, read);

            // asserts
            Check.That(read).IsStrictlyGreaterThan(0);
            Check.That(response).Contains("200 OK");
            Check.That(response).Contains(body);
            Check.That(response).DoesNotContain("408 Request Timeout");

            // dispose
            await server.StopAsync();
        }

        #endregion slow request protection

        #region principal

        [Fact]
        public async Task ConfigurePrincipalResolver_WithBearerToken_Should_Populate_SessionUser() {

            // jwt
            JwtBearerOptions jwtOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: "owner@simplew.dev",
                roles: new[] { "admin", "devops" },
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew"),
                    new IdentityProperty("plan", "pro")
                }
            );

            JwtBearerHelper jwtHelper = new(jwtOptions);

            string token = jwtHelper.CreateToken(
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigurePrincipalResolver(session => {
                if (!jwtHelper.TryAuthenticate(session, out HttpPrincipal principal)) {
                    return null;
                }

                return principal;
            });

            server.MapGet("/api/me", (HttpSession session) => {
                return new {
                    isAuthenticated = session.Principal.IsAuthenticated,
                    identifier = session.Principal.Identity.Identifier,
                    name = session.Principal.Name,
                    email = session.Principal.Email,
                    isAdmin = session.Principal.IsInRole("admin"),
                    tenant = session.Principal.Get("tenant_id"),
                    plan = session.Principal.Get("plan")
                };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/me");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new {
                isAuthenticated = true,
                identifier = "owner",
                name = "Owner",
                email = "owner@simplew.dev",
                isAdmin = true,
                tenant = "simplew",
                plan = "pro"
            }));

            // dispose
            await server.StopAsync();
        }

        [Fact]
        public async Task ConfigurePrincipalResolver_Should_Be_Lazy_Until_SessionUser_IsRead() {

            int resolverCallCount = 0;

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigurePrincipalResolver(session => {
                Interlocked.Increment(ref resolverCallCount);

                return new HttpPrincipal(new HttpIdentity(
                    isAuthenticated: true,
                    authenticationType: "Test",
                    identifier: "lazy-user",
                    name: "Lazy User",
                    email: null,
                    roles: new[] { "admin" },
                    properties: new[] {
                        new IdentityProperty("tenant_id", "simplew")
                    }
                ));
            });

            // route that does NOT read session.User
            server.MapGet("/api/no-user", () => {
                return new { ok = true };
            });

            // route that DOES read session.User
            server.MapGet("/api/with-user", (HttpSession session) => {
                return new {
                    isAuthenticated = session.Principal.IsAuthenticated,
                    identifier = session.Principal.Identity.Identifier,
                    tenant = session.Principal.Get("tenant_id")
                };
            });

            await server.StartAsync();

            var client = new HttpClient();

            // 1) no access to session.User => resolver should not be called
            var response1 = await client.GetAsync($"http://{server.Address}:{server.Port}/api/no-user");
            var content1 = await response1.Content.ReadAsStringAsync();

            Check.That(response1.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content1).IsEqualTo(JsonSerializer.Serialize(new { ok = true }));
            Check.That(resolverCallCount).IsEqualTo(0);

            // 2) access to session.User => resolver should be called exactly once for this request
            var response2 = await client.GetAsync($"http://{server.Address}:{server.Port}/api/with-user");
            var content2 = await response2.Content.ReadAsStringAsync();

            Check.That(response2.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content2).IsEqualTo(JsonSerializer.Serialize(new {
                isAuthenticated = true,
                identifier = "lazy-user",
                tenant = "simplew"
            }));
            Check.That(resolverCallCount).IsEqualTo(1);

            // dispose
            await server.StopAsync();
        }

        #endregion principal

        #region telemetry

        [Fact]
        public void EnableTelemetry_And_DisableTelemetry_Should_Update_Status() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);

            Check.That(server.IsTelemetryEnabled).IsFalse();

            server.EnableTelemetry();
            Check.That(server.IsTelemetryEnabled).IsTrue();

            server.DisableTelemetry();
            Check.That(server.IsTelemetryEnabled).IsFalse();

        }

        [Fact]
        public void ConfigureTelemetry_After_EnableTelemetry_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.EnableTelemetry();

            Assert.Throws<InvalidOperationException>(() => {
                server.ConfigureTelemetry(options => {
                    options.IncludeStackTrace = true;
                });
            });

            server.DisableTelemetry();
        }

        #endregion telemetry

    }

}
