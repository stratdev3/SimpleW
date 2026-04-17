using System.Runtime.CompilerServices;
using SimpleW.Helper.BasicAuth;
using SimpleW.Modules;
using SimpleW.Observability;


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

        private static readonly Func<BasicAuthContext, HttpPrincipal> _defaultHelperPrincipalFactory = new BasicAuthOptions().PrincipalFactory;

        /// <summary>
        /// Adds or updates the BasicAuth convenience module on the current server.
        /// The module is a thin wrapper over BasicAuthHelper that restores the principal
        /// and wires handler-metadata based Basic authentication behavior.
        /// </summary>
        public static SimpleWServer UseBasicAuthModule(this SimpleWServer server, Action<BasicAuthModuleOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            BasicAuthModuleOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            ModuleState state = _states.GetValue(server, static _ => new ModuleState());
            state.SetConfiguration(ModuleConfiguration.FromOptions(options));

            EnsureInstalled(server, state);
            _log.Info($"installed (restorePrincipal={options.RestorePrincipal}, autoAuthorize={options.AutoAuthorize})");
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
        /// Helper options stay available because the module delegates all Basic mechanics to BasicAuthHelper.
        /// </summary>
        public sealed class BasicAuthModuleOptions : BasicAuthOptions {

            /// <summary>
            /// When true, the module restores session.Principal from the Authorization header on every request.
            /// </summary>
            public bool RestorePrincipal { get; set; } = true;

            /// <summary>
            /// When true, handlers decorated with BasicAuthAttribute are automatically enforced.
            /// </summary>
            public bool AutoAuthorize { get; set; } = true;

            /// <summary>
            /// If true, protected OPTIONS requests bypass auth (default true)
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
            /// It receives the current Basic challenge realm in addition to the auth payload.
            /// Ignored if Helper is set.
            /// </summary>
            public Func<BasicAuthModuleContext, HttpPrincipal>? ModulePrincipalFactory { get; set; }

            /// <summary>
            /// Validate and normalize the module options.
            /// </summary>
            /// <returns></returns>
            public new BasicAuthModuleOptions ValidateAndNormalize() {
                if (!RestorePrincipal && AutoAuthorize) {
                    throw new ArgumentException($"{nameof(AutoAuthorize)} requires {nameof(RestorePrincipal)} to be enabled.");
                }

                if (Helper != null) {
                    ValidateHelperExclusivity();
                    return this;
                }

                base.ValidateAndNormalize();
                return this;
            }

            private void ValidateHelperExclusivity() {
                bool hasInlineHelperAuthConfig = (Users?.Length ?? 0) > 0
                                                 || CredentialValidator != null
                                                 || !ReferenceEquals(PrincipalFactory, _defaultHelperPrincipalFactory);

                if (hasInlineHelperAuthConfig || ModulePrincipalFactory != null) {
                    throw new ArgumentException("BasicAuthModuleOptions cannot be combined with inline helper settings, PrincipalFactory, or ModulePrincipalFactory when Helper is provided.");
                }
            }

        }

        /// <summary>
        /// Additional context exposed by the module-level principal factory.
        /// </summary>
        public sealed class BasicAuthModuleContext : BasicAuthContext {

            /// <summary>
            /// Realm used by the module challenge.
            /// </summary>
            public required string Realm { get; init; }

        }

        #region registry / configuration

        /// <summary>
        /// Per-server BasicAuth module state.
        /// </summary>
        private sealed class ModuleState {

            public object SyncRoot { get; } = new();

            public bool IsInstalled { get; set; }

            private volatile ModuleConfiguration? _configuration;

            public ModuleConfiguration? Snapshot => _configuration;

            public void SetConfiguration(ModuleConfiguration configuration) {
                lock (SyncRoot) {
                    _configuration = configuration;
                }
            }

        }

        /// <summary>
        /// Immutable runtime snapshot used by the module.
        /// </summary>
        private sealed record ModuleConfiguration(
            bool RestorePrincipal,
            bool AutoAuthorize,
            bool BypassOptionsRequests,
            BasicAuthHelper Helper
        ) {

            public static ModuleConfiguration FromOptions(BasicAuthModuleOptions options) {
                return new ModuleConfiguration(
                    RestorePrincipal: options.RestorePrincipal,
                    AutoAuthorize: options.AutoAuthorize,
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
                        Realm = ResolveRealm(context.Session)
                    }) ?? options.PrincipalFactory(context);
                });
            }

            private static string ResolveRealm(HttpSession session) {
                return session.Metadata.Get<BasicAuthAttribute>()?.Realm ?? "Restricted";
            }

        }

        #endregion registry / configuration

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
            /// Middleware
            /// </summary>
            /// <param name="session"></param>
            /// <param name="next"></param>
            /// <param name="state"></param>
            /// <returns></returns>
            private static async ValueTask MiddlewareAsync(HttpSession session, Func<ValueTask> next, ModuleState state) {
                ModuleConfiguration? configuration = state.Snapshot;
                if (configuration == null) {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (configuration.RestorePrincipal && configuration.Helper.TryAuthenticate(session, out HttpPrincipal principal)) {
                    session.Principal = principal;
                }

                if (!configuration.AutoAuthorize || session.Metadata.Has<AllowAnonymousAttribute>()) {
                    await next().ConfigureAwait(false);
                    return;
                }

                BasicAuthAttribute? auth = session.Metadata.Get<BasicAuthAttribute>();
                if (auth == null) {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (configuration.BypassOptionsRequests && session.Request.Method == "OPTIONS") {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (!session.Principal.IsAuthenticated) {
                    await configuration.Helper.SendChallengeAsync(session, auth.Realm).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            }

        }

    }

    /// <summary>
    /// Declares that a handler requires HTTP Basic authentication.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class BasicAuthAttribute : Attribute, IHandlerMetadata {

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="realm"></param>
        public BasicAuthAttribute(string realm = "Restricted") {
            Realm = string.IsNullOrWhiteSpace(realm) ? "Restricted" : realm.Trim();
        }

        /// <summary>
        /// Realm sent back to clients in the Basic challenge.
        /// </summary>
        public string Realm { get; }

    }

}
