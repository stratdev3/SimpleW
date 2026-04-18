using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NFluent;
using SimpleW;
using SimpleW.Helper.DependencyInjection;
using SimpleW.Helper.Hosting;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for SimpleW.Helper.DependencyInjection.
    /// </summary>
    public class DependencyInjectionTests {

        [Fact]
        public async Task UseDependencyInjection_AfterStart_Should_Throw() {
            int port = PortManager.GetFreePort();

            await using ServiceProvider rootProvider = new ServiceCollection()
                                                       .BuildServiceProvider();

            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.MapGet("/", () => new { ok = true });

            try {
                await server.StartAsync();

                Assert.Throws<InvalidOperationException>(() => server.UseDependencyInjection(rootProvider));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task MapController_Should_Resolve_A_Scoped_Service_Per_Request_When_Di_Is_Enabled() {
            int port = PortManager.GetFreePort();

            await using ServiceProvider rootProvider = new ServiceCollection()
                                                       .AddScoped<RequestMarker>()
                                                       .BuildServiceProvider();

            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.UseDependencyInjection(rootProvider);
            server.UseMiddleware((session, next) => {
                session.Response.AddHeader("x-request-service", session.GetRequiredRequestService<RequestMarker>().Id.ToString("N"));
                return next();
            });
            server.MapController<ScopedRequestController>("/api");

            try {
                await server.StartAsync();

                var client = new HttpClient();

                var first = await client.GetAsync($"http://{server.Address}:{server.Port}/api/di/scoped");
                var second = await client.GetAsync($"http://{server.Address}:{server.Port}/api/di/scoped");

                string firstHeader = first.Headers.GetValues("x-request-service").Single();
                string secondHeader = second.Headers.GetValues("x-request-service").Single();

                string firstBody = await first.Content.ReadAsStringAsync();
                string secondBody = await second.Content.ReadAsStringAsync();

                string? firstId = JsonDocument.Parse(firstBody).RootElement.GetProperty("id").GetString();
                string? secondId = JsonDocument.Parse(secondBody).RootElement.GetProperty("id").GetString();

                Check.That(first.StatusCode).Is(HttpStatusCode.OK);
                Check.That(second.StatusCode).Is(HttpStatusCode.OK);
                Check.That(firstId).IsEqualTo(firstHeader);
                Check.That(secondId).IsEqualTo(secondHeader);
                Check.That(firstId).IsNotEqualTo(secondId);
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task MapController_Should_Support_OnBeforeMethod_And_Query_Route_Binding_When_Di_Is_Enabled() {
            int port = PortManager.GetFreePort();

            await using ServiceProvider rootProvider = new ServiceCollection()
                                                       .AddScoped<GreetingSuffixService>(_ => new GreetingSuffixService(" from DI"))
                                                       .BuildServiceProvider();

            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.UseDependencyInjection(rootProvider);
            server.MapController<OnBeforeDiController>("/api");

            try {
                await server.StartAsync();

                var client = new HttpClient();
                var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/greet/Chris?prefix=Salut");
                var content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Salut Chris from DI" }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Middleware_Can_Read_Metadata_From_Di_Controller_200() {
            int port = PortManager.GetFreePort();

            await using ServiceProvider rootProvider = new ServiceCollection()
                                                       .BuildServiceProvider();

            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.UseDependencyInjection(rootProvider);
            server.UseMiddleware((session, next) => {
                var current = session.Metadata.Get<TestDiMetadataAttribute>();
                var all = session.Metadata.GetAll<TestDiMetadataAttribute>();

                if (all.Count > 0) {
                    session.Response.AddHeader("x-di-metadata-current", current?.Value ?? string.Empty);
                    session.Response.AddHeader("x-di-metadata-all", string.Join(",", all.Select(static a => a.Value)));
                }

                return next();
            });
            server.MapController<MetadataDiController>("/api");

            try {
                await server.StartAsync();

                var client = new HttpClient();
                var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/meta/hello");
                var content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "metadata" }));
                Check.That(response.Headers.GetValues("x-di-metadata-current").Single()).IsEqualTo("method");
                Check.That(response.Headers.GetValues("x-di-metadata-all").Single()).IsEqualTo("controller,method");
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task MapControllers_Should_Discover_Controllers_From_Base_Type_When_Di_Is_Enabled() {
            int port = PortManager.GetFreePort();

            await using ServiceProvider rootProvider = new ServiceCollection()
                                                       .AddScoped<ScanMessageService>(_ => new ScanMessageService("scan"))
                                                       .BuildServiceProvider();

            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.UseDependencyInjection(rootProvider);
            server.MapControllers<DiScanBaseController>("/api", new[] { typeof(ExcludedDiScanController) });

            try {
                await server.StartAsync();

                var client = new HttpClient();
                var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/scan/ping");
                var content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "scan" }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task MapController_Should_Dispose_Controller_After_Request_When_Di_Is_Enabled() {
            int port = PortManager.GetFreePort();

            DisposableDiController.Reset();

            await using ServiceProvider rootProvider = new ServiceCollection()
                                                       .BuildServiceProvider();

            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.UseDependencyInjection(rootProvider);
            server.MapController<DisposableDiController>("/api");

            try {
                await server.StartAsync();

                var client = new HttpClient();
                var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/dispose/ping");

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(DisposableDiController.DisposeCount).IsEqualTo(1);
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Hosting_ConfigureSimpleW_WithServices_Should_Enable_Controller_Di_Before_MapController() {
            int port = PortManager.GetFreePort();

            var builder = SimpleWHost.CreateApplicationBuilder(Array.Empty<string>());
            builder.UseUrl($"http://127.0.0.1:{port}");
            builder.Services.AddScoped<RequestMarker>();

            builder.ConfigureSimpleW((services, server) => {
                server.UseDependencyInjection(services);
                server.MapController<HostedScopedRequestController>("/api");
            });

            await using IHost host = builder.Build();

            try {
                await host.StartAsync();

                var client = new HttpClient();
                var response = await client.GetAsync($"http://127.0.0.1:{port}/api/hosted/scoped");
                string content = await response.Content.ReadAsStringAsync();
                string? id = JsonDocument.Parse(content).RootElement.GetProperty("id").GetString();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(id).IsNotNull();
                Check.That(id).IsNotEmpty();
            }
            finally {
                await host.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        public sealed class RequestMarker {

            public Guid Id { get; } = Guid.NewGuid();

        }

        [Route("/di")]
        public sealed class ScopedRequestController : Controller {

            private readonly RequestMarker _marker;

            public ScopedRequestController(RequestMarker marker) {
                _marker = marker;
            }

            [Route("GET", "/scoped")]
            public object Get() {
                return new {
                    id = _marker.Id.ToString("N")
                };
            }

        }

        public sealed class GreetingSuffixService {

            public string Value { get; }

            public GreetingSuffixService(string value) {
                Value = value;
            }

        }

        [Route("/greet")]
        public sealed class OnBeforeDiController : Controller {

            private readonly GreetingSuffixService _suffixService;
            private string _suffix = string.Empty;

            public OnBeforeDiController(GreetingSuffixService suffixService) {
                _suffixService = suffixService;
            }

            public override void OnBeforeMethod() {
                _suffix = _suffixService.Value;
            }

            [Route("GET", "/:name")]
            public object Get(string name, string prefix = "Hello") {
                return new {
                    message = $"{prefix} {name}{_suffix}"
                };
            }

        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
        public sealed class TestDiMetadataAttribute : Attribute, IHandlerMetadata {

            public string Value { get; }

            public TestDiMetadataAttribute(string value) {
                Value = value;
            }

        }

        [TestDiMetadata("controller")]
        [Route("/meta")]
        public sealed class MetadataDiController : Controller {

            [TestDiMetadata("method")]
            [Route("GET", "/hello")]
            public object Get() {
                return new {
                    message = "metadata"
                };
            }

        }

        public abstract class DiScanBaseController : Controller {
        }

        public sealed class ScanMessageService {

            public string Message { get; }

            public ScanMessageService(string message) {
                Message = message;
            }

        }

        [Route("/scan")]
        public sealed class DiscoveredDiScanController : DiScanBaseController {

            private readonly ScanMessageService _messageService;

            public DiscoveredDiScanController(ScanMessageService messageService) {
                _messageService = messageService;
            }

            [Route("GET", "/ping")]
            public object Ping() {
                return new {
                    message = _messageService.Message
                };
            }

        }

        [Route("/excluded")]
        public sealed class ExcludedDiScanController : DiScanBaseController {

            [Route("GET", "/ping")]
            public object Ping() {
                return new {
                    message = "excluded"
                };
            }

        }

        [Route("/dispose")]
        public sealed class DisposableDiController : Controller, IDisposable {

            public static int DisposeCount;

            public static void Reset() {
                DisposeCount = 0;
            }

            [Route("GET", "/ping")]
            public object Ping() {
                return new {
                    ok = true
                };
            }

            public void Dispose() {
                Interlocked.Increment(ref DisposeCount);
            }

        }

        [Route("/hosted")]
        public sealed class HostedScopedRequestController : Controller {

            private readonly RequestMarker _marker;

            public HostedScopedRequestController(RequestMarker marker) {
                _marker = marker;
            }

            [Route("GET", "/scoped")]
            public object Get() {
                return new {
                    id = _marker.Id.ToString("N")
                };
            }

        }

    }

}
