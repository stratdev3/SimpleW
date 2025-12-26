using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text;
using static SimpleW.Modules.BasicAuthModuleExtension.BasicAuthOptions;


namespace SimpleW.Modules {

    /// <summary>
    /// BasicAuthModuleExtension
    /// </summary>
    public static class BasicAuthModuleExtension {

        // Tracks which servers already got the middleware installed (per server instance)
        private static readonly ConditionalWeakTable<SimpleWServer, object> _installedServers = new();

        // Registry: ruleset immutable snapshot, swapped on updates
        private static readonly object _rulesLock = new();
        private static volatile RuleSet _rules = RuleSet.Empty;

        /// <summary>
        /// Use Basic Auth Module (adds/updates a prefix rule; installs ONE middleware per server).
        /// It setups a Middleware
        /// </summary>
        public static SimpleWServer UseBasicAuthModule(this SimpleWServer server, Action<BasicAuthOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            BasicAuthOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            UpsertRule(options);

            EnsureInstalled(server);
            return server;
        }

        private static void EnsureInstalled(SimpleWServer server) {
            lock (_installedServers) {
                if (_installedServers.TryGetValue(server, out _)) {
                    return;
                }
                _installedServers.Add(server, new object());
            }

            // Install module exactly once per server instance
            server.UseModule(new BasicAuthModule());
        }

        private static void UpsertRule(BasicAuthOptions o) {
            Rule rule = Rule.FromOptions(o);

            lock (_rulesLock) {
                _rules = _rules.WithUpsert(rule);
            }
        }

        /// <summary>
        /// Basic Auth Options
        /// </summary>
        public sealed class BasicAuthOptions {

            /// <summary>
            /// Apply BasicAuth only for paths starting with this prefix (default "/")
            /// </summary>
            public string Prefix { get; set; } = "/";

            /// <summary>
            /// Realm displayed by clients (default "Restricted")
            /// </summary>
            public string Realm { get; set; } = "Restricted";

            /// <summary>
            /// If true, OPTIONS requests under Prefix bypass auth (default true)
            /// This is useful for CORS preflight requests or API gateways
            /// </summary>
            public bool BypassOptionsRequests { get; set; } = true;

            /// <summary>
            /// Users list (username/password). Ignored if CredentialValidator is set.
            /// </summary>
            public BasicUser[] Users { get; set; } = Array.Empty<BasicUser>();

            /// <summary>
            /// Optional validator (return true to allow). Has priority over Users.
            /// </summary>
            public Func<string, string, bool>? CredentialValidator { get; set; }

            /// <summary>
            /// Validate and normalize
            /// </summary>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            public BasicAuthOptions ValidateAndNormalize() {
                if (string.IsNullOrWhiteSpace(Prefix)) {
                    throw new ArgumentException($"{nameof(BasicAuthOptions)}.{nameof(Prefix)} must not be null or empty.", nameof(Prefix));
                }

                Prefix = NormalizePrefix(Prefix);

                Realm = (Realm ?? string.Empty).Trim();
                if (Realm.Length == 0) {
                    Realm = "Restricted";
                }

                Users = (Users ?? Array.Empty<BasicUser>())
                        .Where(u => u is not null && !string.IsNullOrWhiteSpace(u.Username))
                        .Select(u => new BasicUser((u.Username ?? string.Empty).Trim(), u.Password ?? string.Empty))
                        .Distinct(BasicUserUsernameComparer.Instance)
                        .ToArray();

                return this;
            }

            /// <summary>
            /// Basic User (for AuthBasic)
            /// </summary>
            public sealed class BasicUser {

                /// <summary>
                /// Username
                /// </summary>
                public string Username { get; }

                /// <summary>
                /// Password
                /// </summary>
                public string Password { get; }

                /// <summary>
                /// Constructor
                /// </summary>
                /// <param name="username"></param>
                /// <param name="password"></param>
                public BasicUser(string username, string password) {
                    Username = username;
                    Password = password;
                }
            }

            /// <summary>
            /// BasicUser comparer
            /// </summary>
            private sealed class BasicUserUsernameComparer : IEqualityComparer<BasicUser> {
                public static readonly BasicUserUsernameComparer Instance = new();
                public bool Equals(BasicUser? x, BasicUser? y) => StringComparer.OrdinalIgnoreCase.Equals(x?.Username, y?.Username);
                public int GetHashCode(BasicUser obj) => StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Username ?? string.Empty);
            }

        }

        /// <summary>
        /// Basic Auth Module
        /// </summary>
        private sealed class BasicAuthModule : IHttpModule {

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("BasicAuthModule must be installed before server start.");
                }

                server.UseMiddleware(static (session, next) => MiddlewareAsync(session, next));
            }

            /// <summary>
            /// Middleware
            /// </summary>
            /// <param name="session"></param>
            /// <param name="next"></param>
            /// <returns></returns>
            private static ValueTask MiddlewareAsync(HttpSession session, Func<ValueTask> next) {

                // snapshot ruleset (immutable reference)
                RuleSet rules = _rules;

                if (rules.Prefixes.Length == 0) {
                    return next();
                }

                string path = session.Request.Path;

                // Find first matching prefix (longest first)
                Rule? rule = null;
                foreach (string p in rules.Prefixes) {
                    if (path.StartsWith(p, StringComparison.Ordinal)) {
                        rule = rules.ByPrefix[p];
                        break;
                    }
                }

                // no setup or preflight, follow next in the pipeline
                if (rule is null) {
                    return next();
                }
                if (rule.BypassOptionsRequests && session.Request.Method == "OPTIONS") {
                    return next();
                }

                // Authorization header
                string? auth = session.Request.Headers.Authorization;

                // then call for challenge if something is wrong

                if (string.IsNullOrWhiteSpace(auth)) {
                    return Challenge(session, rule.Realm);
                }
                if (!TryParseBasicAuthorization(auth, out string username, out string password)) {
                    return Challenge(session, rule.Realm);
                }
                if (!rule.IsValid(username, password)) {
                    return Challenge(session, rule.Realm);
                }

                // challenge ok, follow next in the pipeline
                return next();
            }

            /// <summary>
            /// Send a Challenge Response
            /// </summary>
            /// <param name="session"></param>
            /// <param name="realm"></param>
            /// <returns></returns>
            private static ValueTask Challenge(HttpSession session, string realm) {
                return session.Response
                              .Status(401)
                              .AddHeader("WWW-Authenticate", $"Basic realm=\"{EscapeRealm(realm)}\"")
                              .AddHeader("Content-Length", "0")
                              .SendAsync();
            }

            private static string EscapeRealm(string realm) {
                if (realm.IndexOfAny(['"', '\\']) < 0) {
                    return realm;
                }
                return realm.Replace("\\", "\\\\").Replace("\"", "\\\"");
            }

            /// <summary>
            /// Parse: Authorization: Basic base64(username:password)
            /// </summary>
            private static bool TryParseBasicAuthorization(string authorization, out string username, out string password) {
                username = string.Empty;
                password = string.Empty;

                const string scheme = "Basic";
                ReadOnlySpan<char> s = authorization.AsSpan().Trim();

                if (!s.StartsWith(scheme.AsSpan(), StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }

                s = s.Slice(scheme.Length).TrimStart();
                if (s.Length == 0) {
                    return false;
                }

                int maxUtf8 = Encoding.ASCII.GetMaxByteCount(s.Length);
                byte[] b64Bytes = ArrayPool<byte>.Shared.Rent(maxUtf8);

                try {
                    int b64Len = Encoding.ASCII.GetBytes(s, b64Bytes);

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

                        int idx = pair.IndexOf(':');
                        if (idx <= 0) {
                            return false;
                        }

                        username = pair.Substring(0, idx);
                        password = pair.Substring(idx + 1);
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
        }

        /// <summary>
        /// NormalizePrefix
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns></returns>
        internal static string NormalizePrefix(string prefix) {
            prefix = prefix.Trim();
            if (prefix.Length == 0) {
                return "/";
            }
            if (!prefix.StartsWith("/")) {
                prefix = "/" + prefix;
            }
            if (prefix.Length > 1 && prefix.EndsWith("/")) {
                prefix = prefix.TrimEnd('/');
                if (prefix.Length == 0) {
                    prefix = "/";
                }
            }
            return prefix;
        }

        #region registry / ruleset

        /// <summary>
        /// Rule
        /// </summary>
        /// <param name="Prefix"></param>
        /// <param name="Realm"></param>
        /// <param name="BypassOptionsRequests"></param>
        /// <param name="Users"></param>
        /// <param name="Validator"></param>
        private sealed record Rule(string Prefix, string Realm, bool BypassOptionsRequests, Dictionary<string, string> Users, Func<string, string, bool>? Validator) {

            public static Rule FromOptions(BasicAuthOptions o) {
                Dictionary<string, string> dict = new(StringComparer.OrdinalIgnoreCase);
                foreach (BasicUser u in o.Users) {
                    dict[u.Username] = u.Password; // last wins
                }

                return new Rule(
                    Prefix: o.Prefix,
                    Realm: o.Realm,
                    BypassOptionsRequests: o.BypassOptionsRequests,
                    Users: dict,
                    Validator: o.CredentialValidator
                );
            }

            public bool IsValid(string username, string password) {
                if (Validator is not null) {
                    return Validator(username, password);
                }

                // no validator and no users => deny (fail closed)
                if (Users.Count == 0) {
                    return false;
                }

                return Users.TryGetValue(username, out var expected) && string.Equals(expected, password, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// RuleSet
        /// </summary>
        private sealed class RuleSet {
            public static readonly RuleSet Empty = new(Array.Empty<string>(), new Dictionary<string, Rule>(StringComparer.Ordinal));

            public string[] Prefixes { get; }
            public Dictionary<string, Rule> ByPrefix { get; }

            public RuleSet(string[] prefixes, Dictionary<string, Rule> byPrefix) {
                Prefixes = prefixes;
                ByPrefix = byPrefix;
            }

            public RuleSet WithUpsert(Rule rule) {
                // copy dict
                Dictionary<string, Rule> nextDict = new(ByPrefix, StringComparer.Ordinal);
                nextDict[rule.Prefix] = rule;

                // rebuild prefix list sorted by descending length (most specific first)
                string[]? nextPrefixes = nextDict.Keys.OrderByDescending(static p => p.Length).ToArray();

                return new RuleSet(nextPrefixes, nextDict);
            }
        }

        #endregion registry / ruleset

    }
}
