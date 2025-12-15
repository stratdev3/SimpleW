namespace SimpleW {

    /// <summary>
    /// Decorate methods within controllers with this attribute in order to make them callable from the Rest API
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class RouteAttribute : Attribute {

        /// <summary>
        /// Method (GET, POST...)
        /// </summary>
        public string Method { get; protected set; }

        /// <summary>
        /// Path
        /// </summary>
        public string Path { get; protected set; }

        /// <summary>
        /// IsAbsolutePath
        /// </summary>
        public bool IsAbsolutePath { get; protected set; }

        /// <summary>
        /// Description
        /// </summary>
        public string? Description { get; protected set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="RouteAttribute"/> class for Method.
        /// </summary>
        /// <param name="method">The method.</param>
        /// <param name="path">The path.</param>
        /// <param name="isAbsolutePath"></param>
        /// <param name="description">The string description for this route</param>
        /// <exception cref="ArgumentException">The argument 'verb' must be specified.</exception>
        /// <exception cref="ArgumentException">The argument 'path' must be specified.</exception>
        public RouteAttribute(string method, string path, bool isAbsolutePath = false, string? description = null) {
            if (string.IsNullOrWhiteSpace(method)) {
                throw new ArgumentException($"The argument '{nameof(method)}' must be specified.");
            }
            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentException($"The argument '{nameof(path)}' must be specified.");
            }

            Method = method.ToUpperInvariant();
            Path = path;
            IsAbsolutePath = isAbsolutePath;
            Description = description;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RouteAttribute"/> class for Class.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <exception cref="ArgumentException">The argument 'path' must be specified.</exception>
        public RouteAttribute(string path) {
            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentException($"The argument '{nameof(path)}' must be specified.");
            }
            Method = "*";
            Path = path;
        }

        /// <summary>
        /// Add prefix to Path
        /// </summary>
        /// <param name="prefix">The prefix.</param>
        /// <exception cref="ArgumentException">The argument 'prefix' must be specified.</exception>
        public void SetPrefix(string prefix) {
            if (string.IsNullOrWhiteSpace(prefix)) {
                throw new ArgumentException($"The argument '{nameof(prefix)}' must be specified.");
            }

            if (!IsAbsolutePath) {
                Path = prefix + Path;
            }
        }

    }

}
