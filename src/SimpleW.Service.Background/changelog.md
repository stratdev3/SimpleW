# Changelog


## v26.1.0 - _(2026-08-29)_

First stable release, compatible with SimpleW v26.1.x.

### feature

- Add fixed-rate interval schedules with `ScheduleEvery`. The first occurrence runs after one complete interval, missed ticks do not cause schedule drift, and overlapping occurrences are skipped by default.
- Add delayed jobs with `EnqueueAfter` and `EnqueueAt`, backed by a shared timed scheduler that does not occupy worker slots while jobs are waiting.
- Add asynchronous queue backpressure with `EnqueueAsync`, which waits for bounded queue capacity without blocking a thread and keeps admission cancellation separate from cancellation of an accepted job.
- Add `BackgroundJobOptions` with global defaults and per-job or per-schedule overrides for cooperative per-attempt timeouts, retry counts, exponential backoff, maximum retry delays, and timeout retry control.
- Add individual cancellation with `Cancel` for scheduled, queued, retrying, and running jobs. Running delegates receive cancellation cooperatively through `BackgroundJobContext.CancellationToken`.
- Add the `Scheduled`, `Retrying`, and `TimedOut` job states.
- Extend telemetry with attempt, retry, timeout, cancellation, and scheduled-job metrics, plus gauges for enabled interval schedules. Metrics keep low-cardinality `source`, `result`, and `reason` tags.

### change

- Clone `DefaultJobOptions` for each immediate, delayed, cron, or interval job before applying local overrides, so one job cannot mutate shared defaults.
- Keep the non-concurrent schedule guard active while an occurrence is queued, running, or retrying, and release it only when the job reaches a terminal state.
- Reset attempt-scoped progress before a retry and only accept progress reports while user code is running.
- Track delayed jobs and retries in one priority queue, release their captured delegates promptly on cancellation, and cancel all non-terminal work during shutdown.
