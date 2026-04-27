# Background

The [`SimpleW.Service.Background`](https://www.nuget.org/packages/SimpleW.Service.Background) package provides an **in-process background job queue** and a **cron scheduler** for SimpleW.

It is designed for handlers that need to start long-running work without keeping the HTTP request open.


## Why background jobs?

By default, a SimpleW handler **runs to completion** even if the client disconnects. A handler can stop cooperatively by using [`session.RequestAborted`](../guide/handler.md#cancellationtoken-requestaborted), but it must explicitly pass that token to long-running operations or check it.

This is useful when the work should be tied to the HTTP request lifecycle. But for long-running operations such as imports, exports, reports, synchronization, or batch processing, keeping the work inside the handler means the client must keep the HTTP connection open until the final response is ready, typically a `200 OK`.

The background service solves a different problem: the handler can return quickly, usually with `202 Accepted`, while the actual work continues in an in-process background worker.


## Features

This module allows you to:

- enqueue background jobs from handlers, controllers, or the server
- return an HTTP response immediately, typically `202 Accepted`
- track queued, running, succeeded, failed, and canceled jobs
- schedule recurring jobs with cron expressions
- stop workers cleanly when the SimpleW server stops

The queue is **memory-only**. Jobs do not survive process restart.


## Requirements

- .NET 8.0
- SimpleW (core server)
- Cronos, used internally to parse cron expressions


## Installation

```sh
$ dotnet add package SimpleW.Service.Background --version 26.0.0-alpha.20260428-1831
```


## Minimal example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Background;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseBackgroundModule();

server.MapPost("/api/import", (HttpSession session) => {
    string payload = session.Request.BodyString;

    BackgroundJobHandle job = session.GetBackgroundService().Enqueue("import", async ctx => {
        ctx.ReportProgress(0, "Starting import");
        await ImportAsync(payload, ctx.CancellationToken);
    });

    return session.Response.Status(202).Json(new {
        accepted = true,
        jobId = job.Id
    });
});

await server.RunAsync();
```

The handler copies the data it needs, enqueues the work, and returns immediately.


## Configuration options

| Option | Default | Description |
|---|---:|---|
| WorkerCount | `1` | Number of worker loops processing jobs in parallel. Values lower than `1` are normalized to `1`. |
| Capacity | `1024` | Maximum number of queued jobs. When full, `TryEnqueue` returns `false` and `Enqueue` throws. |
| CompletedJobRetention | `1000` | Maximum number of completed job snapshots kept in memory. Set to `0` to remove completed jobs immediately. |
| ShutdownTimeout | `30s` | Maximum time to wait for worker and scheduler loops during server shutdown. |
| TimeZone | `UTC` | Default time zone used by cron schedules. |
| JobStore | `MemoryBackgroundJobStore` | Store used for job snapshots. Can be replaced by a custom implementation. |
| EnableTelemetry | `false` | Enables module telemetry. The underlying `SimpleWServer.Telemetry` must also be enabled. |
| Schedules | `[]` | Cron schedules registered with `options.Schedule(...)`. |


## Register the module

```csharp
server.UseBackgroundModule(options => {
    options.WorkerCount = 2;
    options.Capacity = 500;
    options.CompletedJobRetention = 2000;
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
    options.JobStore = new MemoryBackgroundJobStore();
});
```

The module must be installed before the server starts.


## Enqueue a job from a handler

```csharp
server.MapPost("/api/report", (HttpSession session) => {
    string body = session.Request.BodyString;

    BackgroundJobHandle job = session.GetBackgroundService().Enqueue("report", async ctx => {
        await GenerateReportAsync(body, ctx.CancellationToken);
    });

    return session.Response.Status(202).Json(new {
        jobId = job.Id,
        status = "queued"
    });
});
```

`Enqueue` returns a `BackgroundJobHandle` containing:

| Property | Description |
|---|---|
| Id | Unique job identifier. |
| Name | Job name passed to `Enqueue`. |
| EnqueuedAtUtc | UTC date at which the job entered the queue. |


## Avoid queue overflow

Use `TryEnqueue` when you want to control the response if the queue is full.

```csharp
server.MapPost("/api/export", (HttpSession session) => {
    string payload = session.Request.BodyString;

    bool accepted = session.GetBackgroundService().TryEnqueue("export", async ctx => {
        await ExportAsync(payload, ctx.CancellationToken);
    }, out BackgroundJobHandle job);

    if (!accepted) {
        return session.Response.Status(429).Text("Background queue is full.");
    }

    return session.Response.Status(202).Json(new {
        jobId = job.Id
    });
});
```


## Track jobs

The background service keeps in-memory snapshots of queued, running, and recently completed jobs.

```csharp
server.MapGet("/api/jobs/:id", (HttpSession session, Guid id) => {
    BackgroundJobSnapshot? job = session.GetBackgroundService().GetJob(id);

    if (job == null) {
        return session.Response.NotFound("Job not found.");
    }

    return job;
});
```

Job states:

| Status | Meaning |
|---|---|
| Queued | The job is waiting in the queue. |
| Running | A worker is executing the job. |
| Succeeded | The job completed successfully. |
| Failed | The job threw an exception. |
| Canceled | The server stopped while the job was running. |

Progress fields:

| Property | Description |
|---|---|
| Progress | Latest reported progress percentage, between `0` and `100`, or `null` when unknown. |
| ProgressMessage | Latest reported progress message. |
| UpdatedAtUtc | Last time the job status or progress changed. |


## Report progress

Long-running jobs can report their latest progress from inside the background work.

```csharp
server.MapPost("/api/import", (HttpSession session) => {
    string payload = session.Request.BodyString;

    BackgroundJobHandle job = session.GetBackgroundService().Enqueue("import", async ctx => {
        ctx.ReportProgress(0, "Starting import");

        await ParseAsync(payload, ctx.CancellationToken);
        ctx.ReportProgress(25, "Parsed payload");

        await WriteRowsAsync(ctx.CancellationToken);
        ctx.ReportProgress(80, "Rows written");

        await RefreshCacheAsync(ctx.CancellationToken);
        ctx.ReportProgress(100, "Done");
    });

    return session.Response.Status(202).Json(new {
        jobId = job.Id
    });
});
```

You can also report a message without a percentage:

```csharp
ctx.ReportProgress(message: "Waiting for external API");
ctx.ReportProgress("Still waiting");
```

Progress behavior:

- only the latest progress is stored in the job snapshot
- percentages are normalized to the `0..100` range
- reporting a message without a percentage keeps the previous percentage
- when a job succeeds, an existing percentage lower than `100` is completed to `100`
- failed and canceled jobs keep their last reported progress


## Poll progress from a client

Because progress is included in `BackgroundJobSnapshot`, a client can poll a job endpoint.

```csharp
server.MapGet("/api/jobs/:id", (HttpSession session, Guid id) => {
    BackgroundJobSnapshot? job = session.GetBackgroundService().GetJob(id);

    if (job == null) {
        return session.Response.NotFound("Job not found.");
    }

    return job;
});
```

Example response:

```json
{
  "id": "1aa7e66e-4ec9-4a43-b61a-41da2aa4b3dd",
  "name": "import",
  "source": "handler",
  "status": "Running",
  "progress": 80,
  "progressMessage": "Rows written",
  "updatedAtUtc": "2026-04-27T12:30:10.0000000+00:00"
}
```

## Custom job store

By default, job snapshots are stored in memory with `MemoryBackgroundJobStore`.

You can replace it by implementing `IBackgroundJobStore`.

```csharp
public sealed class MyJobStore : IBackgroundJobStore {

    public void Save(BackgroundJobSnapshot snapshot) {
        // insert or update snapshot
    }

    public bool TryGet(Guid id, out BackgroundJobSnapshot? snapshot) {
        // load one snapshot
    }

    public IReadOnlyCollection<BackgroundJobSnapshot> GetAll() {
        // load retained snapshots
    }

    public bool Remove(Guid id) {
        // remove snapshot
    }

}
```

Then configure the module:

```csharp
server.UseBackgroundModule(options => {
    options.JobStore = new MyJobStore();
});
```

::: info
Important: the current module still executes in-process delegates. A custom store persists the observable job state, progress, and history, but it does not make arbitrary delegate jobs restartable after a process crash.
:::


## Cron schedules

Cron jobs are registered in `UseBackgroundModule`.
When an occurrence is due, the scheduler enqueues a background job in the same queue used by handlers.

```csharp
server.UseBackgroundModule(options => {
    options.Schedule("cleanup", "0 2 * * *", async ctx => {
        await CleanupAsync(ctx.CancellationToken);
    });
});
```

This runs `cleanup` every day at 02:00, using UTC by default.


## Cron with seconds

Cronos supports expressions with seconds when `CronFormat.IncludeSeconds` is enabled.

```csharp
using Cronos;

server.UseBackgroundModule(options => {
    options.Schedule(
        "heartbeat",
        "*/30 * * * * *",
        async ctx => {
            await SendHeartbeatAsync(ctx.CancellationToken);
        },
        cron => {
            cron.CronFormat = CronFormat.IncludeSeconds;
        }
    );
});
```


## Cron time zone

You can set a default time zone for all schedules:

```csharp
server.UseBackgroundModule(options => {
    options.TimeZone = TimeZoneInfo.Local;

    options.Schedule("daily-report", "0 8 * * *", async ctx => {
        await SendDailyReportAsync(ctx.CancellationToken);
    });
});
```

Or override it for one schedule:

```csharp
server.UseBackgroundModule(options => {
    options.Schedule(
        "paris-report",
        "0 8 * * *",
        async ctx => {
            await SendReportAsync(ctx.CancellationToken);
        },
        cron => {
            cron.TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        }
    );
});
```


## Cron concurrency

By default, the same cron schedule does not overlap with itself.

If a previous occurrence is still queued or running, the next occurrence is skipped.

```csharp
server.UseBackgroundModule(options => {
    options.Schedule(
        "slow-sync",
        "*/5 * * * *",
        async ctx => {
            await SyncAsync(ctx.CancellationToken);
        },
        cron => {
            cron.AllowConcurrentExecutions = false;
        }
    );
});
```

To allow overlapping executions:

```csharp
cron.AllowConcurrentExecutions = true;
```


## Use from controllers

Controllers can access the same service.

```csharp
using SimpleW;
using SimpleW.Service.Background;

public sealed class ImportController : Controller {

    [Route("POST", "/import")]
    public object Import() {
        string payload = Request.BodyString;

        BackgroundJobHandle job = this.GetBackgroundService().Enqueue("import", async ctx => {
            await ImportAsync(payload, ctx.CancellationToken);
        });

        return Response.Status(202).Json(new {
            jobId = job.Id
        });
    }

}
```


## Telemetry & Counters

The background module can optionally emit **telemetry and metrics** to help observe queue pressure, job execution, and progress reporting.

Telemetry is disabled by default and also relies on the global SimpleW telemetry system.

```csharp
var server = new SimpleWServer(IPAddress.Any, 8080);

server.EnableTelemetry();

server.UseBackgroundModule(options => {
    options.EnableTelemetry = true;
});
```

When enabled, the module exposes these counters:

| Instrument | Unit | Description |
|---|---|---|
| `simplew.background.job.enqueued.count` | `job` | Jobs accepted into the queue. |
| `simplew.background.job.rejected.count` | `job` | Handler jobs rejected because the queue is full. |
| `simplew.background.job.started.count` | `job` | Jobs picked up by a worker. |
| `simplew.background.job.completed.count` | `job` | Jobs completed with `succeeded`, `failed`, or `canceled`. |
| `simplew.background.job.progress.count` | `report` | Calls to `ctx.ReportProgress(...)`. |

And these duration instruments:

| Instrument | Unit | Description |
|---|---|---|
| `simplew.background.job.queue_wait.duration` | `ms` | Time spent waiting in the queue before execution. |
| `simplew.background.job.run.duration` | `ms` | Time spent executing the job delegate. |

Observable gauges:

| Instrument | Unit | Description |
|---|---|---|
| `simplew.background.queue.length` | `job` | Current number of queued jobs. |
| `simplew.background.queue.capacity` | `job` | Configured queue capacity. |
| `simplew.background.job.running` | `job` | Current number of running jobs. |
| `simplew.background.job.tracked` | `job` | Current number of tracked job snapshots. |
| `simplew.background.worker.count` | `worker` | Configured worker count. |
| `simplew.background.cron.schedule.enabled` | `schedule` | Enabled cron schedules. |

Metric tags are intentionally low-cardinality:

| Tag | Values |
|---|---|
| `source` | `handler`, `cron`, `unknown` |
| `result` | `succeeded`, `failed`, `canceled` |
| `reason` | `queue_full` |

The module does not tag metrics with job ids, job names, cron expressions, or progress messages. This keeps the metric stream safe for production exporters.

Cron occurrences are counted when they are actually enqueued and executed with `source = cron`; skipped cron occurrences and cron enqueue failures are not reported as separate metrics.


## Important behavior notes

- Jobs are in-process and memory-only.
- With the default store, job snapshots are lost when the process stops or crashes.
- A custom `IBackgroundJobStore` can persist snapshots, but not replay arbitrary delegate jobs.
- Cron schedules do not catch up missed occurrences after restart.
- Do not capture `HttpSession`, `HttpRequest`, or `HttpResponse` in background jobs.
- Copy the required request data before calling `Enqueue`.
- The cancellation token is triggered when the background service stops.
- Exceptions are caught by the worker and stored in the job snapshot as `Failed`.
- Progress is a latest-known snapshot, not an event history.
