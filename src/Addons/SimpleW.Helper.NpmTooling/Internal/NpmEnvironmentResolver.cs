namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Selects the runtime mode independently of the build configuration and process environment.
    /// </summary>
    internal static class NpmEnvironmentResolver {

        /// <summary>
        /// Explicit options take precedence over the publication marker on the application assembly.
        /// Unmarked applications use development mode.
        /// </summary>
        internal static NpmEnvironment Resolve(NpmEnvironment configuredEnvironment, bool isPublished) {
            if (configuredEnvironment != NpmEnvironment.AutoDetect) {
                return configuredEnvironment;
            }
            return isPublished ? NpmEnvironment.Production : NpmEnvironment.Development;
        }

    }

}
