namespace SimpleW.Service.Background {

    /// <summary>
    /// In-process background job queue.
    /// </summary>
    public interface IBackgroundService {

        #region immediate enqueue

        /// <summary>
        /// Enqueues a job or throws when the queue is full.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="work"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        BackgroundJobHandle Enqueue(string name, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null);

        /// <summary>
        /// Attempts to enqueue a job.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="work"></param>
        /// <param name="handle"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        bool TryEnqueue(string name, Func<BackgroundJobContext, Task> work, out BackgroundJobHandle handle, Action<BackgroundJobOptions>? configure = null);

        /// <summary>
        /// Asynchronously waits for queue capacity and enqueues a job.
        /// The cancellation token only controls the wait for capacity.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="work"></param>
        /// <param name="configure"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        ValueTask<BackgroundJobHandle> EnqueueAsync(string name, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null, CancellationToken cancellationToken = default);

        #endregion immediate enqueue

        #region delayed enqueue

        /// <summary>
        /// Schedules a job after a delay.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="delay"></param>
        /// <param name="work"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        BackgroundJobHandle EnqueueAfter(string name, TimeSpan delay, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null);

        /// <summary>
        /// Schedules a job at a specific date.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="dueAt"></param>
        /// <param name="work"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        BackgroundJobHandle EnqueueAt(string name, DateTimeOffset dueAt, Func<BackgroundJobContext, Task> work, Action<BackgroundJobOptions>? configure = null);

        #endregion delayed enqueue

        #region control and observation

        /// <summary>
        /// Requests cancellation of a non-terminal job.
        /// </summary>
        /// <param name="id"></param>
        /// <returns>True when the job exists and is not terminal.</returns>
        bool Cancel(Guid id);

        /// <summary>
        /// Gets a job snapshot by id, or null when it is unknown or already trimmed from memory.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        BackgroundJobSnapshot? GetJob(Guid id);

        /// <summary>
        /// Gets all currently tracked jobs.
        /// </summary>
        /// <returns></returns>
        IReadOnlyCollection<BackgroundJobSnapshot> GetJobs();

        #endregion control and observation

    }

}
