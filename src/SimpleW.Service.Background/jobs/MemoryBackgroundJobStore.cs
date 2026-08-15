using System.Collections.Concurrent;


namespace SimpleW.Service.Background {

    /// <summary>
    /// In-memory background job snapshot store.
    /// </summary>
    public sealed class MemoryBackgroundJobStore : IBackgroundJobStore {

        private readonly ConcurrentDictionary<Guid, BackgroundJobSnapshot> _jobs = new();

        /// <summary>
        /// Saves or updates a job snapshot.
        /// </summary>
        /// <param name="snapshot"></param>
        public void Save(BackgroundJobSnapshot snapshot) {
            ArgumentNullException.ThrowIfNull(snapshot);
            _jobs[snapshot.Id] = snapshot;
        }

        /// <summary>
        /// Attempts to get a job snapshot by id.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        public bool TryGet(Guid id, out BackgroundJobSnapshot? snapshot) => _jobs.TryGetValue(id, out snapshot);

        /// <summary>
        /// Gets all currently retained job snapshots.
        /// </summary>
        /// <returns></returns>
        public IReadOnlyCollection<BackgroundJobSnapshot> GetAll() => _jobs.Values.ToArray();

        /// <summary>
        /// Removes a job snapshot.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public bool Remove(Guid id) => _jobs.TryRemove(id, out _);

    }

}
