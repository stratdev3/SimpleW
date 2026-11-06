using System.Reflection;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Selects the runtime mode independently of the build configuration and process environment.
    /// </summary>
    internal static class NpmEnvironmentResolver {

        /// <summary>
        /// Explicit options take precedence over the publication marker on the application assembly.
        /// Missing entry assemblies and unmarked applications use development mode.
        /// </summary>
        internal static NpmEnvironment Resolve(NpmEnvironment configuredEnvironment, Assembly? application) {
            if (configuredEnvironment != NpmEnvironment.AutoDetect) {
                return configuredEnvironment;
            }
            bool published = application?.GetCustomAttributes<AssemblyMetadataAttribute>()
                                         .Any(attribute => attribute.Key == "SimpleW.Published" && attribute.Value == "true") == true;
            return published ? NpmEnvironment.Production : NpmEnvironment.Development;
        }

    }

}
