namespace SimpleW.Service.Background {

    /// <summary>
    /// In-process background job queue.
    /// </summary>
    public interface IBackgroundService {

        /// <summary>
        /// Enqueues a job or throws when the queue is full.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="work"></param>
        /// <returns></returns>
        BackgroundJobHandle Enqueue(string name, Func<BackgroundJobContext, Task> work);

        /// <summary>
        /// Attempts to enqueue a job.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="work"></param>
        /// <param name="handle"></param>
        /// <returns></returns>
        bool TryEnqueue(string name, Func<BackgroundJobContext, Task> work, out BackgroundJobHandle handle);

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

    }

}
