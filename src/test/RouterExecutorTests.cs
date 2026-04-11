using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for RouterExecutor
    /// </summary>
    public class RouterExecutorTests {

        #region before handler

        [Fact]
        public async Task OnBeforeController_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapController<OnBeforeController>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class OnBeforeController : Controller {

            private string Name = "";

            public override void OnBeforeMethod() {
                Name = "Chris";
            }

            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = $"{Name}, Hello World !" };
            }

        }

        #endregion before handler

        #region metadata handler

        [Fact]
        public async Task Middleware_Can_Read_Controller_Metadata_From_Session_200() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseMiddleware((session, next) => {
                var current = session.Metadata.Get<TestMetadataAttribute>();
                var all = session.Metadata.GetAll<TestMetadataAttribute>();

                if (all.Count > 0) {
                    session.Response.AddHeader("x-metadata-current", current?.Value ?? string.Empty);
                    session.Response.AddHeader("x-metadata-all", string.Join(",", all.Select(a => a.Value)));
                }

                return next();
            });

            server.MapController<MetadataController>("/api");

            await server.StartAsync();

            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/meta");
            var content = await response.Content.ReadAsStringAsync();

            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "metadata" }));
            Check.That(response.Headers.GetValues("x-metadata-current").Single()).IsEqualTo("method");
            Check.That(response.Headers.GetValues("x-metadata-all").Single()).IsEqualTo("controller,method");

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Middleware_Can_Read_Delegate_Metadata_From_Session_200() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseMiddleware((session, next) => {
                var all = session.Metadata.GetAll<TestMetadataAttribute>();

                if (all.Count > 0) {
                    session.Response.AddHeader("x-metadata-all", string.Join(",", all.Select(a => a.Value)));
                }

                return next();
            });

            server.MapGet("/delegate", DelegateMetadataHandlers.Annotated);

            await server.StartAsync();

            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/delegate");
            var content = await response.Content.ReadAsStringAsync();

            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "delegate" }));
            Check.That(response.Headers.GetValues("x-metadata-all").Single()).IsEqualTo("delegate-class,delegate-method");

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Metadata_Does_Not_Leak_To_Next_Request_200() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseMiddleware((session, next) => {
                var all = session.Metadata.GetAll<TestMetadataAttribute>();

                if (all.Count > 0) {
                    session.Response.AddHeader("x-metadata-all", string.Join(",", all.Select(a => a.Value)));
                }

                return next();
            });

            server.MapController<MetadataController>("/api");
            server.MapGet("/plain", PlainHandler);

            await server.StartAsync();

            var client = new HttpClient();
            var first = await client.GetAsync($"http://{server.Address}:{server.Port}/api/meta");
            var second = await client.GetAsync($"http://{server.Address}:{server.Port}/plain");
            var secondContent = await second.Content.ReadAsStringAsync();

            Check.That(first.Headers.Contains("x-metadata-all")).IsTrue();
            Check.That(second.StatusCode).Is(HttpStatusCode.OK);
            Check.That(secondContent).IsEqualTo(JsonSerializer.Serialize(new { message = "plain" }));
            Check.That(second.Headers.Contains("x-metadata-all")).IsFalse();

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [TestMetadata("controller")]
        public class MetadataController : Controller {

            [TestMetadata("method")]
            [Route("GET", "/meta")]
            public object Get() {
                return new { message = "metadata" };
            }

        }

        public static object PlainHandler() {
            return new { message = "plain" };
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
        public sealed class TestMetadataAttribute : Attribute, IHandlerMetadata {

            public string Value { get; }

            public TestMetadataAttribute(string value) {
                Value = value;
            }

        }

        [TestMetadata("delegate-class")]
        public static class DelegateMetadataHandlers {

            [TestMetadata("delegate-method")]
            public static object Annotated() {
                return new { message = "delegate" };
            }

        }

        #endregion metadata handler

    }

}
