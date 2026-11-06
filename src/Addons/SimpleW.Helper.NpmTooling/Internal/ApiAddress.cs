using System.Net;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Resolves the API address and the path shared by development CORS and production routing.
    /// </summary>
    internal sealed class ApiAddress {

        private readonly string _value;
        private readonly bool _absolute;

        /// <summary>
        /// Validates an API path or a full HTTP(S) URL without credentials, query or fragment.
        /// </summary>
        /// <param name="value">Configured API address.</param>
        public ApiAddress(string value) {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) || value.IndexOfAny(['\\', '?', '#']) >= 0) {
                throw InvalidAddress();
            }
            _value = value;
            string path;
            if (value.StartsWith('/')) {
                if (value.StartsWith("//", StringComparison.Ordinal)) throw InvalidAddress();
                path = value;
            }
            else {
                if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")
                    || uri.Host.Length == 0 || uri.UserInfo.Length != 0 || uri.Port == 0) {
                    throw InvalidAddress();
                }
                _absolute = true;
                path = uri.AbsolutePath;
            }
            PathPrefix = path.TrimEnd('/');
            if (PathPrefix.Length == 0) PathPrefix = "/";
        }

        /// <summary>
        /// API path without a trailing slash, or / when the API has no dedicated path prefix.
        /// </summary>
        public string PathPrefix { get; }

        /// <summary>Uses a full URL unchanged or appends the configured path to the active listener origin.</summary>
        /// <param name="server">Server after its listener has started, so an ephemeral port is resolved.</param>
        /// <returns>The complete address passed to the frontend.</returns>
        public string Resolve(SimpleWServer server) {
            if (_absolute) {
                return _value;
            }
            IPAddress address = server.Address ?? throw new InvalidOperationException("Set SimpleWNpmApiBaseUrl to a complete HTTP(S) URL in the application's .csproj when using a Unix socket.");
            if (address.Equals(IPAddress.Any)) {
                address = IPAddress.Loopback;
            }
            if (address.Equals(IPAddress.IPv6Any)) {
                address = IPAddress.IPv6Loopback;
            }
            string origin = new UriBuilder(server.Engine.IsEncrypted ? "https" : "http", address.ToString(), server.Port).Uri.GetLeftPart(UriPartial.Authority);
            return origin + _value;
        }

        /// <summary>
        /// Reports an invalid project property consistently in development and production.
        /// </summary>
        /// <returns>The configuration exception.</returns>
        private static ArgumentException InvalidAddress() => new("SimpleWNpmApiBaseUrl must be a path such as /api or a complete HTTP(S) URL, without credentials, whitespace, a query or a fragment.", "SimpleWNpmApiBaseUrl");

    }

}
