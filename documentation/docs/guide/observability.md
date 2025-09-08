# Observability

Observability is integrated through support for **OpenTelemetry**, the [open standard](https://opentelemetry.io/) for distributed tracing and metrics collection.


## Logging

Logs are very useful for developer when coding.
The following example shows how to :
- [`EnableTelemetry `](../reference/simplewserver#telemetry)
- subscribe to all SimpleW `Event` with `openTelemetryObserver()`
- log each request to console with `LogProcessor` (do not use for production).

::: code-group

<<< @/snippets/observability.cs#snippet{csharp:line-numbers} [program.cs]

:::

Open browser to http://localhost:2015/api/test and console will show log.

## Traces

For production, logs alone aren't enougth - we need a full trace for every `Event`.
The most reliable approach is to leverage battle-tested solutions for collecting and managing telemetry data.

The team behind [Uptrace](https://uptrace.dev/) has built an impressive open-source, self-hosted observability platform.
They offer a helper extension for the [.NET OpenTelemetry exporter](https://github.com/open-telemetry/opentelemetry-dotnet) as a [NuGet package](https://www.nuget.org/packages/Uptrace.OpenTelemetry), 
making it effortless to integrate end-to-end tracing into your .NET services.

So add their package

```sh
$ dotnet add package Uptrace.OpenTelemetry
```

And see the following example, especially the `openTelemetryObserver()`

::: code-group

<<< @/snippets/observability-advanced.cs#snippet{29 csharp:line-numbers} [program.cs]

:::

### A screenshot of Uptrace Traces & Logs

SimpleW export many informations (Request, Response, Exception...) and you can view hit, monitor response time or error and searching specific data.

![uptrace tracing](/snippets/observability-with-uptrace.png)
