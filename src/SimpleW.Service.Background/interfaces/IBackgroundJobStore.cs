namespace SimpleW.Service.Background {

    /// <summary>
    /// Stores background job snapshots.
    /// </summary>
    public interface IBackgroundJobStore {

        /// <summary>
        /// Saves or updates a job snapshot.
        /// </summary>
        /// <param name="snapshot"></param>
        void Save(BackgroundJobSnapshot snapshot);

        /// <summary>
        /// Attempts to get a job snapshot by id.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="snapshot"></param>
        /// <returns></returns>
        bool TryGet(Guid id, out BackgroundJobSnapshot? snapshot);

        /// <summary>
        /// Gets all currently retained job snapshots.
        /// </summary>
        /// <returns></returns>
        IReadOnlyCollection<BackgroundJobSnapshot> GetAll();

        /// <summary>
        /// Removes a job snapshot.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        bool Remove(Guid id);

    }

}
