using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SimpleW;


namespace example;

internal sealed class TelemetryScenario : IScenario {

    private const string SourceName = "SimpleW.example";

    public string Name => "telemetry";

    public string Description => "SimpleW traces and metrics with local trace output and optional OTLP export.";

    public string Usage => " [--otlp-endpoint uri] [--otlp-headers value]";

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("otlp-endpoint", "otlp-headers");

        Uri? endpoint = arguments.GetAbsoluteUri("otlp-endpoint");
        string? headers = arguments.Get("otlp-headers");
        if (headers != null && endpoint == null) {
            throw new ExampleCliException("Option '--otlp-headers' requires '--otlp-endpoint'.");
        }

        TracerProviderBuilder tracerBuilder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("SimpleW.example"))
            .AddSource(SourceName)
            .AddProcessor(new ConsoleActivityProcessor());
        MeterProviderBuilder meterBuilder = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("SimpleW.example"))
            .AddMeter(SourceName);

        if (endpoint != null) {
            tracerBuilder.AddOtlpExporter(options => ConfigureExporter(options, endpoint, headers));
            meterBuilder.AddOtlpExporter((options, _) => ConfigureExporter(options, endpoint, headers));
        }

        using TracerProvider tracerProvider = tracerBuilder.Build();
        using MeterProvider meterProvider = meterBuilder.Build();

        SimpleWServer server = ExampleServer.Create(arguments);
        server.ConfigureTelemetry(options => {
            options.InstanceId = "example";
            options.IncludeStackTrace = true;
        });
        server.EnableTelemetry();
        server.MapGet("/", () => new {
            message = "Telemetry is enabled.",
            local = "Completed requests are printed to the console.",
            otlp = endpoint?.AbsoluteUri
        });
        server.MapGet("/api/work", async () => {
            await Task.Delay(25).ConfigureAwait(false);
            return new { completed = true, at = DateTimeOffset.UtcNow };
        });

        await ExampleServer.RunAsync(server, "http", arguments, cancellationToken).ConfigureAwait(false);
    }

    private static void ConfigureExporter(OtlpExporterOptions options, Uri endpoint, string? headers) {
        options.Endpoint = endpoint;
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        if (!string.IsNullOrWhiteSpace(headers)) {
            options.Headers = headers;
        }
    }

    private sealed class ConsoleActivityProcessor : BaseProcessor<Activity> {

        public override void OnEnd(Activity activity) {
            ExampleConsole.Trace(
                activity.GetTagItem("http.request.method")?.ToString(),
                activity.GetTagItem("url.path")?.ToString(),
                activity.GetTagItem("http.response.status_code")?.ToString(),
                activity.Duration.TotalMilliseconds
            );
        }
    }

}
