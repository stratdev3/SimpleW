using System;
using System.Net;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Uptrace.OpenTelemetry;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // subscribe to all SimpleW events
            openTelemetryObserver("SimpleW");

            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }

        static TracerProvider openTelemetryObserver(string source) {
            return Sdk.CreateTracerProviderBuilder()
                              .AddSource(source)
                              // see https://uptrace.dev/get/get-started.html#dsn
                              .AddUptrace("uptrace_connection_string_api_key")
                              .SetResourceBuilder(
                                  ResourceBuilder
                                      .CreateEmpty()
                                      .AddService(serviceName: "Sample", serviceVersion: "0.1")
                              ).Build();
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