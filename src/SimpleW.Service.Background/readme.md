# SimpleW.Service.Background

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

In-process background jobs and cron scheduling for SimpleW.

## Getting Started

```cs
using SimpleW;
using SimpleW.Service.Background;

var server = new SimpleWServer(System.Net.IPAddress.Any, 8080);

server.UseBackgroundModule(options => {
    options.WorkerCount = 2;
    options.JobStore = new MemoryBackgroundJobStore();

    options.Schedule("cleanup", "0 2 * * *", async ctx => {
        await CleanupAsync(ctx.CancellationToken);
    });
});

server.MapPost("/import", (HttpSession session) => {
    string payload = session.Request.BodyString;

    BackgroundJobHandle job = session.GetBackgroundService().Enqueue("import", async ctx => {
        ctx.ReportProgress(0, "Starting import");
        await ImportAsync(payload, ctx.CancellationToken);
        ctx.ReportProgress(100, "Done");
    });

    return session.Response.Status(202).Json(new {
        accepted = true,
        jobId = job.Id
    });
});

await server.RunAsync();
```

Jobs are kept in memory only. They do not survive process restart.

Use `ctx.ReportProgress(percent, message)` from inside a job to update the latest progress exposed by `GetJob(jobId)`.

Telemetry is optional and follows the SimpleW telemetry switch:

```cs
server.EnableTelemetry();

server.UseBackgroundModule(options => {
    options.EnableTelemetry = true;
});
```

When enabled, the module emits low-cardinality counters and gauges such as `simplew.background.job.enqueued.count`, `simplew.background.job.completed.count`, `simplew.background.queue.length`, and `simplew.background.job.running`.

Metrics are tagged only with stable values like `source`, `result`, and `reason`. Job ids, job names, cron expressions, and progress messages are not used as metric tags.

Use `options.JobStore` with a custom `IBackgroundJobStore` to persist job snapshots in another backend. The current module still executes in-process delegates; durable replay after restart will need a future typed-job/payload model.
