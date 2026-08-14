# Background <Badge type="tip" text="Official" />

The [`SimpleW.Service.Background`](https://www.nuget.org/packages/SimpleW.Service.Background) package provides an **in-process background job queue**, delayed jobs, retries, timeouts, and recurring cron or fixed-interval schedules for SimpleW.

It is designed for handlers that need to start long-running work without keeping the HTTP request open.


## Why background jobs?

By default, a SimpleW handler **runs to completion** even if the client disconnects. A handler can stop cooperatively by using [`session.RequestAborted`](../guide/handler.md#cancellationtoken-requestaborted), but it must explicitly pass that token to long-running operations or check it.

This is useful when the work should be tied to the HTTP request lifecycle. But for long-running operations such as imports, exports, reports, synchronization, or batch processing, keeping the work inside the handler means the client must keep the HTTP connection open until the final response is ready, typically a `200 OK`.

The background service solves a different problem: the handler can return quickly, usually with `202 Accepted`, while the actual work continues in an in-process background worker.


## Features

This module allows you to:

- enqueue background jobs from handlers, controllers, or the server
- wait asynchronously for queue capacity with backpressure
- return an HTTP response immediately, typically `202 Accepted`
- retry failed or timed out jobs with deterministic exponential backoff
- cancel delayed, queued, retrying, or running jobs by id
- track scheduled, queued, running, retrying, succeeded, failed, timed out, and canceled jobs
- schedule recurring jobs with cron expressions or fixed intervals
- stop workers cleanly when the SimpleW server stops

The queue is **memory-only**. Jobs do not survive process restart.


## Requirements

- .NET 8.0, 9.0, or 10.0
- SimpleW (core server)
- Cronos (automatically included), used internally to parse cron expressions


## Installation

```sh
$ dotnet add package SimpleW.Service.Background
```

See the [changelog](./service-background-changelog.md)


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
| DefaultJobOptions | See below | Default timeout and retry policy copied by every job and recurring occurrence. |
| Schedules | `[]` | Cron schedules registered with `options.Schedule(...)`. |
| Intervals | `[]` | Fixed-interval schedules registered with `options.ScheduleEvery(...)`. |

Default job options:

| Option | Default | Description |
|---|---:|---|
| Timeout | `null` | Maximum duration of one attempt. `null` disables the timeout. |
| RetryCount | `0` | Number of additional attempts after the first failure. |
| RetryDelay | `1s` | Delay before the first retry. |
| RetryBackoffFactor | `2` | Multiplier applied after each failed attempt. |
| RetryMaxDelay | `1m` | Maximum delay between attempts. |
| RetryOnTimeout | `true` | Allows timed out attempts to use the retry policy. |


## Register the module

```csharp
server.UseBackgroundModule(options => {
    options.WorkerCount = 2;
    options.Capacity = 500;
    options.CompletedJobRetention = 2000;
    options.ShutdownTimeout = TimeSpan.FromSeconds(20);
    options.JobStore = new MemoryBackgroundJobStore();

    options.DefaultJobOptions.Timeout = TimeSpan.FromMinutes(5);
    options.DefaultJobOptions.RetryCount = 2;
    options.DefaultJobOptions.RetryDelay = TimeSpan.FromSeconds(1);
});
```

The module must be installed before the server starts.


## Enqueue a job

### Enqueue from a handler

```csharp
server.MapPost("/api/report", (HttpSession session) => {
    string body = session.Request.BodyString;

    BackgroundJobHandle job = session.GetBackgroundService()
                                     .Enqueue("report", async ctx => {
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
| EnqueuedAtUtc | UTC date at which the job record was created. |


### Avoid queue overflow

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


### Wait for queue capacity

`EnqueueAsync` applies asynchronous backpressure instead of rejecting the job when the bounded queue is full. It does not block a worker thread while waiting.

```csharp
server.MapPost("/api/export", async (HttpSession session) => {
    string payload = session.Request.BodyString;

    using CancellationTokenSource acceptanceTimeout = new(TimeSpan.FromSeconds(5));

    BackgroundJobHandle job = await session.GetBackgroundService()
                                           .EnqueueAsync(
        "export",
        ctx => ExportAsync(payload, ctx.CancellationToken),
        cancellationToken: acceptanceTimeout.Token
    );

    return session.Response.Status(202).Json(new {
        jobId = job.Id
    });
});
```

The supplied `CancellationToken` controls **only the wait for queue capacity**. Once the job is accepted, cancel it with `Cancel(jobId)`. Canceling admission removes the temporary job record and does not leave a snapshot behind.


## Delayed jobs

Use `EnqueueAfter` to execute a job after a relative delay:

```csharp
IBackgroundService background = server.GetBackgroundService();

BackgroundJobHandle reminder = background.EnqueueAfter(
    "send-reminder",
    TimeSpan.FromMinutes(15),
    ctx => SendReminderAsync(ctx.CancellationToken)
);
```

Use `EnqueueAt` for an absolute date:

```csharp
BackgroundJobHandle closing = background.EnqueueAt(
    "close-period",
    new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero),
    ctx => ClosePeriodAsync(ctx.CancellationToken)
);
```

A delayed job remains `Scheduled` until its due date. A zero delay or a date in the past attempts immediate queue admission. If the queue is full when a delayed job becomes due, it waits asynchronously for capacity instead of being discarded.

Delayed delegates remain in memory and are lost if the process stops.


## Retry and timeout

Every enqueue method accepts a per-job configuration callback. It starts from `DefaultJobOptions`, then applies the local overrides.

```csharp
BackgroundJobHandle import = background.Enqueue(
    "import",
    ctx => ImportAsync(ctx.CancellationToken),
    job => {
        job.Timeout = TimeSpan.FromMinutes(2);
        job.RetryCount = 3;
        job.RetryDelay = TimeSpan.FromSeconds(1);
        job.RetryBackoffFactor = 2;
        job.RetryMaxDelay = TimeSpan.FromSeconds(30);
        job.RetryOnTimeout = true;
    }
);
```

`RetryCount` is the number of **additional** attempts. With `RetryCount = 3`, the job can run at most four times.

Retry delay uses deterministic exponential backoff:

```text
Retry 1: 1 second
Retry 2: 2 seconds
Retry 3: 4 seconds
```

The delay is capped by `RetryMaxDelay`. A retry keeps the same job id, releases its worker during the delay, changes the snapshot to `Retrying`, and resets attempt-scoped progress.

Timeout applies independently to every attempt. It is cooperative: the module cancels `ctx.CancellationToken`, but .NET cannot forcibly terminate a delegate that ignores that token. A retry starts only after the timed out delegate has returned.


## Cancel a job

Use the id returned in `BackgroundJobHandle`:

```csharp
bool cancellationRequested = background.Cancel(import.Id);
```

Cancellation behavior depends on the current state:

- `Scheduled`, `Queued`, or `Retrying` jobs become `Canceled` immediately
- `Running` jobs receive cancellation through `ctx.CancellationToken` and become `Canceled` when the delegate returns
- cancellation is never retried
- the method returns `false` for an unknown or already terminal job


## Track jobs

The background service keeps snapshots of scheduled, queued, running, retrying, and recently completed jobs.

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
| Scheduled | The job is waiting for its delayed execution date. |
| Queued | The job is waiting in the queue. |
| Running | A worker is executing the job. |
| Retrying | The job is waiting for its next attempt without occupying a worker. |
| Succeeded | The job completed successfully. |
| Failed | The job exhausted its retries after an exception. |
| TimedOut | The final attempt exceeded its timeout and no timeout retry remains. |
| Canceled | The job was canceled individually or during server shutdown. |

Important snapshot fields:

| Property | Description |
|---|---|
| Id | Stable identifier preserved across all attempts. |
| Source | Job origin: `handler`, `cron`, or `interval`. |
| EnqueuedAtUtc | Date at which the job record was created. |
| ScheduledAtUtc | Initial due date of a delayed job. |
| NextAttemptAtUtc | Due date of the pending retry. |
| StartedAtUtc | Date at which the first attempt started. |
| FinishedAtUtc | Date at which the job reached a terminal state. |
| Attempt | Current or last completed attempt number, starting at `1`. |
| MaxAttempts | Maximum attempts including the initial execution. |
| Error | Terminal failure or timeout message. |
| LastError | Error produced by the latest failed attempt, including jobs that later succeed. |
| Timeout | Timeout applied independently to each attempt. |
| Progress | Latest reported progress percentage, between `0` and `100`, or `null` when unknown. |
| ProgressMessage | Latest reported progress message. |
| UpdatedAtUtc | Last time the job status or progress changed. |
| Duration | Elapsed time since the first attempt started, frozen at terminal completion. |


## Track progress

### Report progress

Long-running jobs can report their latest progress from inside the background work.

```csharp
server.MapPost("/api/import", (HttpSession session) => {
    string payload = session.Request.BodyString;

    BackgroundJobHandle job = session.GetBackgroundService()
                                     .Enqueue("import", async ctx => {
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
- progress is reset before each retry because it belongs to the failed attempt
- failed and canceled jobs keep their last reported progress


### Poll progress from a client

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


## Recurring jobs

### Fixed-interval schedules

Use `ScheduleEvery` for recurring work expressed as a duration instead of a cron expression:

```csharp
server.UseBackgroundModule(options => {
    options.ScheduleEvery(
        "refresh-cache",
        TimeSpan.FromMinutes(10),
        ctx => RefreshCacheAsync(ctx.CancellationToken),
        interval => {
            interval.AllowConcurrentExecutions = false;
            interval.JobOptions.Timeout = TimeSpan.FromMinutes(2);
            interval.JobOptions.RetryCount = 1;
        }
    );
});
```

The first occurrence runs after one complete interval. Subsequent occurrences follow fixed ticks calculated from the planned cadence rather than from the previous completion time.

By default, an occurrence is skipped while the previous occurrence is queued, running, or retrying. Set `AllowConcurrentExecutions = true` only when overlapping executions are safe.

Missed interval ticks are not replayed after a long execution or process restart.


### Cron schedules

Cron jobs are registered in `UseBackgroundModule`.
When an occurrence is due, the scheduler enqueues a background job in the same queue used by handlers.

```csharp
server.UseBackgroundModule(options => {
    options.Schedule(
        "cleanup",
        "0 2 * * *",
        async ctx => {
            await CleanupAsync(ctx.CancellationToken);
        },
        cron => {
            cron.AllowConcurrentExecutions = false;
            cron.JobOptions.Timeout = TimeSpan.FromMinutes(2);
            cron.JobOptions.RetryCount = 1;
        }
    );
});
```

This runs `cleanup` every day at 02:00, using UTC by default.

By default, an occurrence is skipped while the previous occurrence is queued, running, or retrying. Set `AllowConcurrentExecutions = true` only when overlapping executions are safe.


### Cron with seconds

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


### Cron time zone

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

## Use from controllers

Controllers can access the same service.

```csharp
using SimpleW;
using SimpleW.Service.Background;

public sealed class ImportController : Controller {

    [Route("POST", "/import")]
    public object Import() {
        string payload = Request.BodyString;

        BackgroundJobHandle job = this.GetBackgroundService()
                                      .Enqueue("import", async ctx => {
            await ImportAsync(payload, ctx.CancellationToken);
        });

        return Response.Status(202).Json(new {
            jobId = job.Id
        });
    }

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
| `simplew.background.job.enqueued.count` | `job` | Work items admitted to the ready queue, including retries. |
| `simplew.background.job.rejected.count` | `job` | Immediate enqueue operations rejected because the queue is full. |
| `simplew.background.job.started.count` | `job` | Jobs that started their first attempt. |
| `simplew.background.job.attempted.count` | `attempt` | Attempts started, including retries. |
| `simplew.background.job.attempt.completed.count` | `attempt` | Attempts completed, tagged with their result. |
| `simplew.background.job.completed.count` | `job` | Jobs that reached a terminal state. |
| `simplew.background.job.progress.count` | `report` | Calls to `ctx.ReportProgress(...)`. |
| `simplew.background.job.retried.count` | `retry` | Retries scheduled after exceptions or timeouts. |
| `simplew.background.job.timed_out.count` | `attempt` | Attempts that exceeded their configured timeout. |
| `simplew.background.job.canceled.count` | `job` | Jobs that reached `Canceled`. |

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
| `simplew.background.job.scheduled` | `job` | Delayed jobs and retries waiting in the timed scheduler. |
| `simplew.background.job.tracked` | `job` | Current number of tracked job snapshots. |
| `simplew.background.worker.count` | `worker` | Configured worker count. |
| `simplew.background.cron.schedule.enabled` | `schedule` | Enabled cron schedules. |
| `simplew.background.interval.schedule.enabled` | `schedule` | Enabled fixed-interval schedules. |

Metric tags are intentionally low-cardinality:

| Tag | Values |
|---|---|
| `source` | `handler`, `cron`, `interval`, `unknown` |
| `result` | `succeeded`, `failed`, `timed_out`, `canceled`, `retry` |
| `reason` | `queue_full`, `exception`, `timeout` |

The module does not tag metrics with job ids, job names, cron expressions, or progress messages. This keeps the metric stream safe for production exporters.

Recurring occurrences are counted when they are actually enqueued. Skipped cron or interval occurrences are logged but are not reported as separate metrics.


## Important behavior notes

- Jobs are in-process and memory-only.
- With the default store, job snapshots are lost when the process stops or crashes.
- A custom `IBackgroundJobStore` can persist snapshots, but not replay arbitrary delegate jobs.
- Delayed jobs and retry delays do not survive process restart.
- Cron and fixed-interval schedules do not catch up missed occurrences after restart.
- Do not capture `HttpSession`, `HttpRequest`, or `HttpResponse` in background jobs.
- Copy the required request data before calling `Enqueue`.
- The job cancellation token is triggered by individual cancellation, per-attempt timeout, or server shutdown.
- Cancellation and timeout are cooperative; delegates must observe `ctx.CancellationToken`.
- Exceptions and timeouts use the configured retry policy before becoming terminal.
- Retry delays and delayed jobs do not occupy worker slots.
- Progress is a latest-known snapshot, not an event history.
