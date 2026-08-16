namespace SimpleW {

    /// <summary>
    /// Exception raised when a listener reload fails.
    /// </summary>
    public sealed class ListenerReloadException : Exception {

        /// <summary>
        /// Gets the exception raised by the listener reload.
        /// </summary>
        public Exception ReloadException { get; }

        /// <summary>
        /// Gets the exception raised while restoring the previous listener, or null when it was restored.
        /// </summary>
        public Exception? RollbackException { get; }

        /// <summary>
        /// Gets whether the previous listener was restored successfully.
        /// </summary>
        public bool ListenerRestored => RollbackException == null;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="reloadException"></param>
        /// <param name="rollbackException"></param>
        public ListenerReloadException(Exception reloadException, Exception? rollbackException = null)
            : base(
                rollbackException == null
                    ? "Listener reload failed, but the previous listener was restored."
                    : "Listener reload failed and the previous listener could not be restored.",
                reloadException
            ) {
            ArgumentNullException.ThrowIfNull(reloadException);
            ReloadException = reloadException;
            RollbackException = rollbackException;
        }

    }

}
