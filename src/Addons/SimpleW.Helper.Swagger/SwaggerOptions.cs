namespace SimpleW.Helper.Swagger {

    /// <summary>
    /// Options for Swagger helper
    /// </summary>
    public sealed class SwaggerOptions {

        /// <summary>
        /// OpenAPI title
        /// </summary>
        public string Title { get; set; } = "SimpleW API";

        /// <summary>
        /// OpenAPI version string (info.version)
        /// </summary>
        public string Version { get; set; } = "v1";

        /// <summary>
        /// Optional description
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Filter which routes appear in swagger (null => all routes)
        /// </summary>
        public Func<RouteInfo, bool>? RouteFilter { get; set; }

        /// <summary>
        /// Best effort: scan Controller methods to infer query params types
        /// </summary>
        public bool ScanControllersForParameters { get; set; } = true;

        /// <summary>
        /// Customize swagger UI HTML (advanced)
        /// </summary>
        public Func<string /*swaggerJson*/, string /*html*/>? UiHtmlFactory { get; set; }

        /// <summary>
        /// Check Properties and return
        /// </summary>
        /// <returns></returns>
        internal SwaggerOptions ValidateAndNormalize() {
            Title = string.IsNullOrWhiteSpace(Title) ? "SimpleW API" : Title.Trim();
            Version = string.IsNullOrWhiteSpace(Version) ? "v1" : Version.Trim();
            return this;
        }

    }

}
