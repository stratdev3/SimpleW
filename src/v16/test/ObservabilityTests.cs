using System.Diagnostics;
using System.Net;
using System.Text.Json;
using NFluent;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Observability
    /// </summary>
    public class ObservabilityTests {

        #region should_be_false

        public static bool ResultShouldBeFalse = false;

        [Fact]
        public async Task Observability_ShouldBeDisable() {

            // subscribe to all SimpleW events
            openTelemetryObserverFalse("SimpleW");

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // telemetry is disable by default

            server.AddDynamicContent(typeof(TelemetryController), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            await Task.Delay(50); // give a little time to logger

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(ResultShouldBeFalse).IsFalse();

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        public static TracerProvider openTelemetryObserverFalse(string source) {
            return Sdk.CreateTracerProviderBuilder()
                              .AddSource(source)
                              .AddProcessor(new LogProcessorFalse())
                              .SetResourceBuilder(
                                  ResourceBuilder
                                      .CreateEmpty()
                                      .AddService(serviceName: "Sample", serviceVersion: "0.1")
                              ).Build();
        }

        public class LogProcessorFalse : BaseProcessor<Activity> {
            public override void OnEnd(Activity activity) {
                ObservabilityTests.ResultShouldBeFalse = true;
                //Console.WriteLine($"{activity.GetTagItem("http.request.method")} \"{activity.GetTagItem("url.full")}\" {activity.GetTagItem("http.response.status_code")} {(int)activity.Duration.TotalMilliseconds}ms session-{activity.GetTagItem("session")} {activity.GetTagItem("client.address")} \"{activity.GetTagItem("user_agent.original")}\"");
            }
        }

        #endregion should_be_false

        #region should_be_true

        public static bool ResultShouldBeTrue = false;

        [Fact]
        public async Task Observability_ShouldBeTrue() {

            // subscribe to all SimpleW events
            openTelemetryObserverTrue("SimpleW");

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // enable telemetry
            server.EnableTelemetry = true;

            server.AddDynamicContent(typeof(TelemetryController), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            await Task.Delay(50); // give a little time to logger

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(ResultShouldBeTrue).IsTrue();

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        public static TracerProvider openTelemetryObserverTrue(string source) {
            return Sdk.CreateTracerProviderBuilder()
                              .AddSource(source)
                              .AddProcessor(new LogProcessorTrue())
                              .SetResourceBuilder(
                                  ResourceBuilder
                                      .CreateEmpty()
                                      .AddService(serviceName: "Sample", serviceVersion: "0.1")
                              ).Build();
        }

        public class LogProcessorTrue : BaseProcessor<Activity> {
            public override void OnEnd(Activity activity) {
                ObservabilityTests.ResultShouldBeTrue = true;
                //Console.WriteLine($"{activity.GetTagItem("http.request.method")} \"{activity.GetTagItem("url.full")}\" {activity.GetTagItem("http.response.status_code")} {(int)activity.Duration.TotalMilliseconds}ms session-{activity.GetTagItem("session")} {activity.GetTagItem("client.address")} \"{activity.GetTagItem("user_agent.original")}\"");
            }
        }

        #endregion should_be_true

        [Route("/test")]
        public class TelemetryController : Controller {
            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

    }

}
