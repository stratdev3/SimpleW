# SimpleW.Service.Background

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![License](https://img.shields.io/badge/license-MIT-7737d1.svg)](https://github.com/Stratdev3/SimpleW/blob/master/licence)
[![NuGet](https://img.shields.io/nuget/v/SimpleW.Service.Background?color=7737d1&label=version&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.Background)
[![Downloads](https://img.shields.io/nuget/dt/SimpleW.Service.Background?color=7737d1&label=downloads&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Service.Background)

In-process background jobs and cron scheduling for SimpleW.

## Getting Started

```cs
using SimpleW;
using SimpleW.Service.Background;

var server = new SimpleWServer(System.Net.IPAddress.Any, 8080);

server.UseBackgroundModule(options => {
    options.WorkerCount = 2;
    options.JobStore = new MemoryBackgroundJobStore();
    options.DefaultJobOptions.Timeout = TimeSpan.FromMinutes(5);

    options.Schedule("cleanup", "0 2 * * *", async ctx => {
        await CleanupAsync(ctx.CancellationToken);
    });

    options.ScheduleEvery("refresh-cache", TimeSpan.FromMinutes(10), async ctx => {
        await RefreshCacheAsync(ctx.CancellationToken);
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

## Delayed and interval jobs

Schedule one in-process job after a delay or at a specific date:

```cs
IBackgroundService background = server.GetBackgroundService();

BackgroundJobHandle delayed = background.EnqueueAfter(
    "send-reminder",
    TimeSpan.FromMinutes(15),
    ctx => SendReminderAsync(ctx.CancellationToken)
);

BackgroundJobHandle scheduled = background.EnqueueAt(
    "close-period",
    new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero),
    ctx => ClosePeriodAsync(ctx.CancellationToken)
);
```

`ScheduleEvery` uses fixed ticks and runs for the first time after one complete interval. By default, a tick is skipped while the previous occurrence is queued, running, or retrying. Set `AllowConcurrentExecutions` when overlapping occurrences are explicitly safe:

```cs
server.UseBackgroundModule(options => {
    options.ScheduleEvery(
        "poll-inbox",
        TimeSpan.FromSeconds(30),
        ctx => PollInboxAsync(ctx.CancellationToken),
        schedule => {
            schedule.AllowConcurrentExecutions = false;
            schedule.JobOptions.Timeout = TimeSpan.FromSeconds(20);
        }
    );
});
```

## Retry and timeout

Defaults are copied from `BackgroundOptions.DefaultJobOptions`, then each job can override them:

```cs
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

`RetryCount` is the number of additional attempts. A job keeps the same id across every attempt. Backoff delays do not occupy a worker.

Timeout and cancellation are cooperative. The module cancels `ctx.CancellationToken`, but it cannot forcibly stop a delegate that ignores that token. A retry never starts while the previous attempt is still executing.

## Cancellation and asynchronous enqueue

Cancel delayed, queued, retrying, or running jobs by id:

```cs
bool cancellationRequested = background.Cancel(import.Id);
```

For backpressure, `EnqueueAsync` waits asynchronously until the bounded queue has capacity:

```cs
using CancellationTokenSource acceptanceTimeout = new(TimeSpan.FromSeconds(5));

BackgroundJobHandle accepted = await background.EnqueueAsync(
    "large-export",
    ctx => ExportAsync(ctx.CancellationToken),
    cancellationToken: acceptanceTimeout.Token
);
```

The token passed to `EnqueueAsync` controls only the wait for queue capacity. After acceptance, use `Cancel(jobId)` to cancel the job.

Telemetry is optional and follows the SimpleW telemetry switch:

```cs
server.ConfigureTelemetry(options => options.Enabled = true);

server.UseBackgroundModule(options => {
    options.EnableTelemetry = true;
});
```

When enabled, the module emits low-cardinality counters and gauges such as `simplew.background.job.enqueued.count`, `simplew.background.job.completed.count`, `simplew.background.job.retried.count`, `simplew.background.job.timed_out.count`, `simplew.background.queue.length`, and `simplew.background.job.running`.

Metrics are tagged only with stable values like `source`, `result`, and `reason`. Job ids, job names, cron expressions, and progress messages are not used as metric tags.

Use `options.JobStore` with a custom `IBackgroundJobStore` to persist job snapshots in another backend. The current module still executes in-process delegates; durable replay after restart will need a future typed-job/payload model.
