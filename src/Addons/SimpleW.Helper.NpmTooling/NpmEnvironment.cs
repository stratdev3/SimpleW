namespace SimpleW.Helper.NpmTooling {

    /// <summary>
    /// Selects how the frontend is hosted.
    /// </summary>
    public enum NpmEnvironment {

        /// <summary>
        /// Uses Production for a published application and Development for a regular build.
        /// </summary>
        AutoDetect = 0,

        /// <summary>
        /// Starts and supervises the frontend through npm.
        /// </summary>
        Development,

        /// <summary>
        /// Serves the compiled frontend files.
        /// </summary>
        Production
    }
}
