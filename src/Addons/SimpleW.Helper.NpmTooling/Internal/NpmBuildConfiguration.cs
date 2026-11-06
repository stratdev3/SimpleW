using System.Reflection;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Shared MSBuild settings embedded in the application during compilation.
    /// </summary>
    internal sealed record NpmBuildConfiguration(string SourcePath, string PublishPath, string ApiBaseUrl, bool IsPublished) {

        /// <summary>
        /// Reads the application's configuration and publication marker together, without external settings files.
        /// </summary>
        internal static NpmBuildConfiguration Read(Assembly? application) {
            AssemblyMetadataAttribute[] metadata = application?.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray() ?? [];

            string Required(string key) {
                string? value = metadata.FirstOrDefault(attribute => attribute.Key == key)?.Value;
                if (string.IsNullOrEmpty(value)) {
                    throw new InvalidOperationException($"SimpleW.Helper.NpmTooling: missing '{key}' in the application assembly. Configure the frontend paths and API URL in the application's .csproj and rebuild with the SimpleW.Helper.NpmTooling NuGet package.");
                }
                return value;
            }

            return new NpmBuildConfiguration(
                Required("SimpleW.Npm.SourcePath"),
                Required("SimpleW.Npm.PublishPath"),
                Required("SimpleW.Npm.ApiBaseUrl"),
                metadata.Any(attribute => attribute.Key == "SimpleW.Published" && attribute.Value == "true"));
        }

    }

}
