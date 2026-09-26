namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// File browser module extension methods.
    /// </summary>
    public static class FileBrowserModuleExtension {

        #region module registration

        /// <summary>
        /// Adds a file browser and chunked upload endpoint to the server.
        /// Configure the server request body limit before calling this method.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The upload threshold or chunk size exceeds the server request body limit.</exception>
        public static SimpleWServer UseFileBrowserModule(this SimpleWServer server, Action<FileBrowserOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            FileBrowserOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new FileBrowserModule(options));
            return server;
        }

        #endregion module registration

    }

}
