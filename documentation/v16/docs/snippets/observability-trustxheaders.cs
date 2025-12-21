using System;
using System.Net;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // subscribe to all SimpleW events
            openTelemetryObserver("SimpleW");

            var server = new SimpleWServer(IPAddress.Any, 2015);

            // enable telemetry
            server.EnableTelemetry = true;

            // allow X Headers
            server.TrustXHeaders = true;

            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }

        static TracerProvider openTelemetryObserver(string source) {
            return Sdk.CreateTracerProviderBuilder()
                              .AddSource(source)
                              .AddProcessor(new LogProcessor()) // custom log processor
                              .SetResourceBuilder(
                                  ResourceBuilder
                                      .CreateEmpty()
                                      .AddService(serviceName: "Sample", serviceVersion: "0.1")
                              ).Build();
        }

        // custom log processor for opentelemetry
        class LogProcessor : BaseProcessor<Activity> {
            // write log to console
            public override void OnEnd(Activity activity) {
                // WARNING : use for local debug only not production
                Console.WriteLine($"{activity.GetTagItem("http.request.method")} \"{activity.GetTagItem("url.full")}\" {activity.GetTagItem("http.response.status_code")} {(int)activity.Duration.TotalMilliseconds}ms session-{activity.GetTagItem("session")} {activity.GetTagItem("client.address")} \"{activity.GetTagItem("user_agent.original")}\"");
            }
        }

    }

    public class SomeController : Controller {
        [Route("GET", "/test")]
        public object SomePublicMethod() {
            return new {
                message = "Hello World !"
            };
        }
    }

}