# Changelog


## Unreleased

Maintenance: compatibility with SimpleW v26.1.x.



## v26.0.0 - _(2026-05-??)_

Initial release of `SimpleW.Service.Background`.

### feature

- Initial `SimpleW.Service.Background` package release for SimpleW v26.
- Add in-process background jobs and cron scheduling for SimpleW.
- Add fixed-interval schedules with `ScheduleEvery`.
- Add delayed jobs with `EnqueueAfter` and `EnqueueAt`.
- Add retry policies, cooperative per-attempt timeouts, and individual cancellation.
- Add asynchronous queue backpressure with `EnqueueAsync`.
