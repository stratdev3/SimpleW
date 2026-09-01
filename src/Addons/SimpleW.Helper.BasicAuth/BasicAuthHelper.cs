using System.Buffers;
using System.Buffers.Text;
using System.Text;
using SimpleW.Observability;


namespace SimpleW.Helper.BasicAuth {

    /// <summary>
    /// Stateless helper for HTTP Basic authentication.
    /// It parses the Authorization header, validates the credentials,
    /// and can build a SimpleW principal when authentication succeeds.
    /// Policy decisions such as route protection remain in user middleware.
    /// </summary>
    public sealed class BasicAuthHelper {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<BasicAuthHelper>();

        private readonly Dictionary<string, string> _users;
        private readonly Func<string, string, bool>? _credentialValidator;
        private readonly Func<BasicAuthContext, HttpPrincipal> _principalFactory;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="configure"></param>
        public BasicAuthHelper(Action<BasicAuthOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            BasicAuthOptions options = new();
            configure(options);
            options.ValidateAndNormalize();

            _credentialValidator = options.CredentialValidator;
            _principalFactory = options.PrincipalFactory;
            _users = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (BasicUser user in options.Users) {
                _users[user.Username] = user.Password;
            }
        }

        /// <summary>
        /// Authenticate the current request using the Authorization header.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="principal"></param>
        /// <returns></returns>
        public bool TryAuthenticate(HttpSession session, out HttpPrincipal principal) {
            ArgumentNullException.ThrowIfNull(session);
            principal = HttpPrincipal.Anonymous;

            try {
                string? authorization = session.Request.Headers.Authorization;
                if (string.IsNullOrWhiteSpace(authorization)) {
                    _log.Trace(() => "TryAuthenticate : missing header authorization");
                    return false;
                }

                if (!TryParseBasicAuthorization(authorization, out string username, out string password)) {
                    _log.Trace(() => "TryAuthenticate : unable to parse header authorization");
                    return false;
                }

                if (!IsValid(username, password)) {
                    _log.Trace(() => "TryAuthenticate : invalid username/password");
                    return false;
                }

                principal = _principalFactory.Invoke(new BasicAuthContext() {
                    Session = session,
                    Username = username,
                    Password = password
                });
                return true;
            }
            catch (Exception ex) {
                _log.Warn("TryAuthenticate", ex);
                return false;
            }
        }

        /// <summary>
        /// Send a Chanllenge (401 Basic challenge response).
        /// </summary>
        /// <param name="session"></param>
        /// <param name="realm"></param>
        /// <returns></returns>
        public ValueTask SendChallengeAsync(HttpSession session, string realm = "Restricted") {
            ArgumentNullException.ThrowIfNull(session);

            string effectiveRealm = string.IsNullOrWhiteSpace(realm) ? "Restricted" : realm.Trim();

            return session.Response
                          .Status(401)
                          .AddHeader("WWW-Authenticate", $"Basic realm=\"{EscapeRealm(effectiveRealm)}\"")
                          .AddHeader("Content-Length", "0")
                          .SendAsync();
        }

        /// <summary>
        /// True if username/password is valid
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        private bool IsValid(string username, string password) {
            if (_credentialValidator != null) {
                return _credentialValidator(username, password);
            }

            if (_users.Count == 0) {
                return false;
            }

            return _users.TryGetValue(username, out string? expected) && string.Equals(expected, password, StringComparison.Ordinal);
        }

        /// <summary>
        /// Parse Authorization: Basic base64(username:password)
        /// </summary>
        /// <param name="authorization"></param>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        private static bool TryParseBasicAuthorization(string authorization, out string username, out string password) {
            username = string.Empty;
            password = string.Empty;

            const string scheme = "Basic";
            ReadOnlySpan<char> span = authorization.AsSpan().Trim();

            if (!span.StartsWith(scheme.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (span.Length <= scheme.Length || !char.IsWhiteSpace(span[scheme.Length])) {
                return false;
            }

            span = span.Slice(scheme.Length).TrimStart();
            if (span.Length == 0) {
                return false;
            }

            int maxUtf8 = Encoding.ASCII.GetMaxByteCount(span.Length);
            byte[] b64Bytes = ArrayPool<byte>.Shared.Rent(maxUtf8);

            try {
                int b64Len = Encoding.ASCII.GetBytes(span, b64Bytes);
                byte[] decoded = ArrayPool<byte>.Shared.Rent(b64Len);

                try {
                    OperationStatus status = Base64.DecodeFromUtf8(
                        new ReadOnlySpan<byte>(b64Bytes, 0, b64Len),
                        decoded,
                        out _,
                        out int written
                    );

                    if (status != OperationStatus.Done || written <= 0) {
                        return false;
                    }

                    string pair = Encoding.UTF8.GetString(decoded, 0, written);
                    int separatorIndex = pair.IndexOf(':');
                    if (separatorIndex <= 0) {
                        return false;
                    }

                    username = pair.Substring(0, separatorIndex);
                    password = pair.Substring(separatorIndex + 1);
                    return true;
                }
                finally {
                    ArrayPool<byte>.Shared.Return(decoded);
                }
            }
            catch {
                return false;
            }
            finally {
                ArrayPool<byte>.Shared.Return(b64Bytes);
            }
        }

        /// <summary>
        /// Espace Realm
        /// </summary>
        /// <param name="realm"></param>
        /// <returns></returns>
        private static string EscapeRealm(string realm) {
            if (realm.IndexOfAny(['"', '\\']) < 0) {
                return realm;
            }

            return realm.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

    }

    /// <summary>
    /// Options for BasicAuthHelper.
    /// </summary>
    public class BasicAuthOptions {

        /// <summary>
        /// Static users list used when no custom validator is provided.
        /// </summary>
        public BasicUser[] Users { get; set; } = Array.Empty<BasicUser>();

        /// <summary>
        /// Optional credentials validator.
        /// Return true to accept the provided username/password pair.
        /// </summary>
        public Func<string, string, bool>? CredentialValidator { get; set; }

        /// <summary>
        /// Principal factory.
        /// </summary>
        public Func<BasicAuthContext, HttpPrincipal> PrincipalFactory { get; set; } = (BasicAuthContext context) => {
            return new HttpPrincipal(new HttpIdentity(
                isAuthenticated: true,
                authenticationType: "Basic",
                identifier: context.Username,
                name: context.Username,
                email: null,
                roles: null,
                properties: [
                    new IdentityProperty("login", context.Username),
                    new IdentityProperty("auth_scheme", "Basic"),
                    new IdentityProperty("auth_time", DateTime.UtcNow.ToString("O"))
                ]
            ));
        };

        /// <summary>
        /// Validate and normalize options.
        /// </summary>
        /// <returns></returns>
        public BasicAuthOptions ValidateAndNormalize() {
            Users = (Users ?? Array.Empty<BasicUser>())
                        .Where(static u => u != null && !string.IsNullOrWhiteSpace(u.Username))
                        .Select(static u => new BasicUser((u.Username ?? string.Empty).Trim(), u.Password ?? string.Empty))
                        .GroupBy(static u => u.Username, StringComparer.OrdinalIgnoreCase)
                        .Select(static g => g.Last())
                        .ToArray();

            return this;
        }

    }

    /// <summary>
    /// Basic user definition.
    /// </summary>
    /// <param name="Username"></param>
    /// <param name="Password"></param>
    public sealed record BasicUser(string Username, string Password);

    /// <summary>
    /// Authentication context passed to the principal factory.
    /// </summary>
    public class BasicAuthContext {

        /// <summary>
        /// Current session.
        /// </summary>
        public required HttpSession Session { get; init; }

        /// <summary>
        /// User name extracted from the Authorization header.
        /// </summary>
        public required string Username { get; init; }

        /// <summary>
        /// Password extracted from the Authorization header.
        /// </summary>
        public required string Password { get; init; }

    }

}
