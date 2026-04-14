using System.Buffers;
using System.Runtime.CompilerServices;
using SimpleW.Modules;
using SimpleW.Observability;
using SimpleW.Helper.BasicAuth;


namespace SimpleW.Service.BasicAuth {

    /// <summary>
    /// BasicAuthModuleExtension
    /// </summary>
    public static class BasicAuthModuleExtension {

        private static readonly ConditionalWeakTable<SimpleWServer, ModuleState> _states = new();

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<BasicAuthModule>();

        /// <summary>
        /// Adds or updates a BasicAuth prefix rule on the current server.
        /// The module is a convenience wrapper over BasicAuthHelper.
        /// </summary>
        public static SimpleWServer UseBasicAuthModule(this SimpleWServer server, Action<BasicAuthModuleOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            BasicAuthModuleOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            ModuleState state = _states.GetValue(server, static _ => new ModuleState());
            state.Upsert(Rule.FromOptions(options));

            _log.Info($"installed with prefix {options.Prefix}");
            EnsureInstalled(server, state);
            return server;
        }

        private static void EnsureInstalled(SimpleWServer server, ModuleState state) {
            lock (state.SyncRoot) {
                if (state.IsInstalled) {
                    return;
                }
                state.IsInstalled = true;
            }

            server.UseModule(new BasicAuthModule(state));
        }

        /// <summary>
        /// Module-level Basic Auth configuration.
        /// Prefix/realm live here, while actual Basic authentication is delegated to a helper.
        /// </summary>
        public sealed class BasicAuthModuleOptions : BasicAuthOptions {

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
            /// This is useful for CORS preflight requests or API gateways.
            /// </summary>
            public bool BypassOptionsRequests { get; set; } = true;

            /// <summary>
            /// Optional pre-built helper.
            /// When set, inline auth properties cannot be used.
            /// </summary>
            public BasicAuthHelper? Helper { get; set; }

            /// <summary>
            /// Optional module-level principal factory.
            /// It receives the matched prefix and realm in addition to the auth payload.
            /// Ignored if Helper is set.
            /// </summary>
            public Func<BasicAuthModuleContext, HttpPrincipal>? ModulePrincipalFactory { get; set; }

            /// <summary>
            /// Validate and normalize the module options.
            /// </summary>
            /// <returns></returns>
            public new BasicAuthModuleOptions ValidateAndNormalize() {
                if (string.IsNullOrWhiteSpace(Prefix)) {
                    throw new ArgumentException($"{nameof(BasicAuthModuleOptions)}.{nameof(Prefix)} must not be null or empty.", nameof(Prefix));
                }

                Prefix = SimpleWExtension.NormalizePrefix(Prefix);
                Realm = string.IsNullOrWhiteSpace(Realm) ? "Restricted" : Realm.Trim();

                if (Helper != null) {
                    bool hasInlineHelperAuthConfig = (Users?.Length ?? 0) > 0
                                                     || CredentialValidator != null
                                                     || !ReferenceEquals(PrincipalFactory, _defaultHelperPrincipalFactory);

                    if (hasInlineHelperAuthConfig || ModulePrincipalFactory != null) {
                        throw new ArgumentException("BasicAuthModuleOptions cannot be combined with Users, CredentialValidator, PrincipalFactory, or ModulePrincipalFactory.");
                    }

                    return this;
                }

                base.ValidateAndNormalize();
                return this;
            }

            private static readonly Func<BasicAuthContext, HttpPrincipal> _defaultHelperPrincipalFactory = new BasicAuthOptions().PrincipalFactory;

        }

        /// <summary>
        /// Additional context exposed by the module-level principal factory.
        /// </summary>
        public sealed class BasicAuthModuleContext : BasicAuthContext {

            /// <summary>
            /// Prefix matched by the module.
            /// </summary>
            public required string Prefix { get; init; }

            /// <summary>
            /// Realm used by the module challenge.
            /// </summary>
            public required string Realm { get; init; }

        }

        #region registry / ruleset

        /// <summary>
        /// Per-server BasicAuth module state.
        /// </summary>
        private sealed class ModuleState {

            public object SyncRoot { get; } = new();

            public bool IsInstalled { get; set; }

            private volatile RuleSet _rules = RuleSet.Empty;

            public RuleSet Snapshot => _rules;

            public void Upsert(Rule rule) {
                lock (SyncRoot) {
                    _rules = _rules.WithUpsert(rule);
                }
            }

        }

        /// <summary>
        /// BasicAuth rule stored by prefix.
        /// </summary>
        private sealed record Rule(string Prefix, string Realm, bool BypassOptionsRequests, BasicAuthHelper Helper) {

            public static Rule FromOptions(BasicAuthModuleOptions options) {
                return new Rule(
                    Prefix: options.Prefix,
                    Realm: options.Realm,
                    BypassOptionsRequests: options.BypassOptionsRequests,
                    Helper: options.Helper ?? CreateHelper(options)
                );
            }

            private static BasicAuthHelper CreateHelper(BasicAuthModuleOptions options) {
                return new BasicAuthHelper(o => {
                    o.Users = options.Users;
                    o.CredentialValidator = options.CredentialValidator;
                    o.PrincipalFactory = context => options.ModulePrincipalFactory?.Invoke(new BasicAuthModuleContext {
                        Session = context.Session,
                        Username = context.Username,
                        Password = context.Password,
                        Prefix = options.Prefix,
                        Realm = options.Realm
                    }) ?? options.PrincipalFactory(context);
                });
            }

        }

        /// <summary>
        /// Immutable rules snapshot.
        /// </summary>
        private sealed class RuleSet {

            public static readonly RuleSet Empty = new(Array.Empty<string>(), new Dictionary<string, Rule>(StringComparer.Ordinal));

            public string[] Prefixes { get; }
            public Dictionary<string, Rule> ByPrefix { get; }

            public RuleSet(string[] prefixes, Dictionary<string, Rule> byPrefix) {
                Prefixes = prefixes;
                ByPrefix = byPrefix;
            }

            public Rule? Find(string path) {
                foreach (string prefix in Prefixes) {
                    if (path.StartsWith(prefix, StringComparison.Ordinal)) {
                        return ByPrefix[prefix];
                    }
                }

                return null;
            }

            public RuleSet WithUpsert(Rule rule) {
                Dictionary<string, Rule> nextDict = new(ByPrefix, StringComparer.Ordinal);
                nextDict[rule.Prefix] = rule;

                string[] nextPrefixes = nextDict.Keys.OrderByDescending(static p => p.Length).ToArray();
                return new RuleSet(nextPrefixes, nextDict);
            }
        }

        #endregion registry / ruleset

        /// <summary>
        /// Module wrapper that delegates all Basic authentication work to BasicAuthHelper.
        /// </summary>
        private sealed class BasicAuthModule : IHttpModule {

            /// <summary>
            /// State
            /// </summary>
            private readonly ModuleState _state;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="state"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public BasicAuthModule(ModuleState state) {
                _state = state ?? throw new ArgumentNullException(nameof(state));
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("BasicAuthModule must be installed before server start.");
                }

                server.UseMiddleware((session, next) => MiddlewareAsync(session, next, _state));
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="session"></param>
            /// <param name="next"></param>
            /// <param name="state"></param>
            /// <returns></returns>
            private static ValueTask MiddlewareAsync(HttpSession session, Func<ValueTask> next, ModuleState state) {
                RuleSet rules = state.Snapshot;
                if (rules.Prefixes.Length == 0) {
                    return next();
                }

                Rule? rule = rules.Find(session.Request.Path);
                if (rule == null) {
                    return next();
                }

                if (rule.BypassOptionsRequests && session.Request.Method == "OPTIONS") {
                    return next();
                }

                if (!rule.Helper.TryAuthenticate(session, out HttpPrincipal principal)) {
                    return rule.Helper.SendChallengeAsync(session, rule.Realm);
                }
                session.Principal = principal;

                return next();
            }

        }

    }
}
