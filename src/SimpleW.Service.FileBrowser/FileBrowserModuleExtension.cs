namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// File browser module extension methods.
    /// </summary>
    public static class FileBrowserModuleExtension {

        /// <summary>
        /// Adds a file browser and chunked upload endpoint to the server.
        /// </summary>
        public static SimpleWServer UseFileBrowserModule(this SimpleWServer server, Action<FileBrowserOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            FileBrowserOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new FileBrowserModule(options));
            return server;
        }

    }

}
