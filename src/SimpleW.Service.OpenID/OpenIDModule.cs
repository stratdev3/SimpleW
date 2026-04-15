using System.Runtime.CompilerServices;
using SimpleW.Helper.OpenID;
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.OpenID {

    /// <summary>
    /// OpenID module extensions for SimpleW.
    /// </summary>
    public static class OpenIDModuleExtension {

        private static readonly ConditionalWeakTable<SimpleWServer, ModuleState> _states = new();

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<OpenIDModule>();

        private static readonly OpenIDHelperOptions _defaultHelperOptions = new();
        private static readonly Func<OpenIDPrincipalContext, HttpPrincipal> _defaultHelperPrincipalFactory = _defaultHelperOptions.PrincipalFactory;

        /// <summary>
        /// Adds or updates the OpenID convenience module on the current server.
        /// The module is a thin wrapper over OpenIDHelper that restores the principal,
        /// wires handler-metadata based challenge behavior, and maps technical routes.
        /// </summary>
        public static SimpleWServer UseOpenIDModule(this SimpleWServer server, Action<OpenIDModuleOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            OpenIDModuleOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            ModuleState state = _states.GetValue(server, static _ => new ModuleState());
            state.SetConfiguration(ModuleConfiguration.FromOptions(options));

            EnsureInstalled(server, state);
            _log.Info($"installed with base path {state.Snapshot!.BasePath}");
            return server;
        }

        private static void EnsureInstalled(SimpleWServer server, ModuleState state) {
            ModuleConfiguration configuration = state.Snapshot ?? throw new InvalidOperationException("OpenID module configuration is missing.");

            lock (state.SyncRoot) {
                if (state.IsInstalled) {
                    return;
                }

                state.IsInstalled = true;
            }

            server.UseModule(new OpenIDModule(state, configuration.LoginPath, configuration.CallbackPath, configuration.LogoutPath));
        }

        /// <summary>
        /// Module-level OpenID configuration.
        /// Helper options stay available because the module delegates all OpenID mechanics to OpenIDHelper.
        /// </summary>
        public sealed class OpenIDModuleOptions : OpenIDHelperOptions {

            /// <summary>
            /// Base path used for technical OpenID routes (default "/auth/oidc")
            /// </summary>
            public string BasePath { get; set; } = "/auth/oidc";

            /// <summary>
            /// When true, the module restores session.Principal from the OpenID cookie on every request.
            /// </summary>
            public bool RestorePrincipal { get; set; } = true;

            /// <summary>
            /// When true, handlers decorated with OpenIDAuthAttribute automatically trigger an OpenID login redirect.
            /// </summary>
            public bool AutoChallenge { get; set; } = true;

            /// <summary>
            /// Optional pre-built helper.
            /// When set, inline helper properties cannot be used.
            /// </summary>
            public OpenIDHelper? Helper { get; set; }

            /// <summary>
            /// Optional module-level principal factory.
            /// It receives the module route information in addition to the validated OpenID claims.
            /// Ignored when Helper is set.
            /// </summary>
            public Func<OpenIDModuleContext, HttpPrincipal>? ModulePrincipalFactory { get; set; }

            /// <summary>
            /// Validate and normalize the module options.
            /// </summary>
            /// <returns></returns>
            public new OpenIDModuleOptions ValidateAndNormalize() {
                if (string.IsNullOrWhiteSpace(BasePath)) {
                    throw new ArgumentException($"{nameof(OpenIDModuleOptions)}.{nameof(BasePath)} must not be null or empty.", nameof(BasePath));
                }

                BasePath = SimpleWExtension.NormalizePrefix(BasePath);

                if (!RestorePrincipal && AutoChallenge) {
                    throw new ArgumentException($"{nameof(AutoChallenge)} requires {nameof(RestorePrincipal)} to be enabled.");
                }

                if (Helper != null) {
                    ValidateHelperExclusivity();
                    return this;
                }

                base.ValidateAndNormalize();
                return this;
            }

            private void ValidateHelperExclusivity() {
                bool hasInlineHelperConfig = Providers.Count > 0
                                             || !string.Equals(CookieName, _defaultHelperOptions.CookieName, StringComparison.Ordinal)
                                             || !string.Equals(ChallengeCookieNamePrefix, _defaultHelperOptions.ChallengeCookieNamePrefix, StringComparison.Ordinal)
                                             || !string.Equals(CookiePath, _defaultHelperOptions.CookiePath, StringComparison.Ordinal)
                                             || !string.Equals(CookieDomain, _defaultHelperOptions.CookieDomain, StringComparison.Ordinal)
                                             || CookieSecure != _defaultHelperOptions.CookieSecure
                                             || CookieHttpOnly != _defaultHelperOptions.CookieHttpOnly
                                             || CookieSameSite != _defaultHelperOptions.CookieSameSite
                                             || SessionLifetime != _defaultHelperOptions.SessionLifetime
                                             || ChallengeLifetime != _defaultHelperOptions.ChallengeLifetime
                                             || BackchannelTimeout != _defaultHelperOptions.BackchannelTimeout
                                             || Backchannel != null
                                             || AllowExternalReturnUrls != _defaultHelperOptions.AllowExternalReturnUrls
                                             || CookieProtectionKey != null
                                             || !Equals(PrincipalFactory, _defaultHelperPrincipalFactory);

                if (hasInlineHelperConfig || ModulePrincipalFactory != null) {
                    throw new ArgumentException("OpenIDModuleOptions cannot be combined with inline helper settings, PrincipalFactory, or ModulePrincipalFactory when Helper is provided.");
                }
            }

        }

        /// <summary>
        /// Additional context exposed by the module-level principal factory.
        /// </summary>
        public sealed class OpenIDModuleContext : OpenIDPrincipalContext {

            /// <summary>
            /// Base path used by the module.
            /// </summary>
            public required string BasePath { get; init; }

            /// <summary>
            /// Login route mapped by the module.
            /// </summary>
            public required string LoginPath { get; init; }

            /// <summary>
            /// Callback route mapped by the module.
            /// </summary>
            public required string CallbackPath { get; init; }

            /// <summary>
            /// Logout route mapped by the module.
            /// </summary>
            public required string LogoutPath { get; init; }

        }

        #region registry / configuration

        /// <summary>
        /// Per-server OpenID module state.
        /// </summary>
        private sealed class ModuleState {

            public object SyncRoot { get; } = new();

            public bool IsInstalled { get; set; }

            private volatile ModuleConfiguration? _configuration;

            public ModuleConfiguration? Snapshot => _configuration;

            public void SetConfiguration(ModuleConfiguration configuration) {
                lock (SyncRoot) {
                    if (_configuration != null && !string.Equals(_configuration.BasePath, configuration.BasePath, StringComparison.Ordinal)) {
                        throw new InvalidOperationException($"OpenID module base path is already fixed to '{_configuration.BasePath}' for this server.");
                    }

                    _configuration = configuration;
                }
            }

        }

        /// <summary>
        /// Immutable runtime snapshot used by the module.
        /// </summary>
        private sealed record ModuleConfiguration(
            string BasePath,
            string LoginPath,
            string CallbackPath,
            string LogoutPath,
            bool RestorePrincipal,
            bool AutoChallenge,
            OpenIDHelper Helper
        ) {

            public static ModuleConfiguration FromOptions(OpenIDModuleOptions options) {
                string basePath = options.BasePath;
                string loginPath = BuildRoutePath(basePath, "login/:provider");
                string callbackPath = BuildRoutePath(basePath, "callback/:provider");
                string logoutPath = BuildRoutePath(basePath, "logout");

                OpenIDHelper helper = options.Helper ?? CreateHelper(options, basePath, loginPath, callbackPath, logoutPath);

                return new ModuleConfiguration(
                    BasePath: basePath,
                    LoginPath: loginPath,
                    CallbackPath: callbackPath,
                    LogoutPath: logoutPath,
                    RestorePrincipal: options.RestorePrincipal,
                    AutoChallenge: options.AutoChallenge,
                    Helper: helper
                );
            }

            private static OpenIDHelper CreateHelper(
                OpenIDModuleOptions options,
                string basePath,
                string loginPath,
                string callbackPath,
                string logoutPath
            ) {
                return new OpenIDHelper(o => {
                    o.CookieName = options.CookieName;
                    o.ChallengeCookieNamePrefix = options.ChallengeCookieNamePrefix;
                    o.CookiePath = options.CookiePath;
                    o.CookieDomain = options.CookieDomain;
                    o.CookieSecure = options.CookieSecure;
                    o.CookieHttpOnly = options.CookieHttpOnly;
                    o.CookieSameSite = options.CookieSameSite;
                    o.SessionLifetime = options.SessionLifetime;
                    o.ChallengeLifetime = options.ChallengeLifetime;
                    o.BackchannelTimeout = options.BackchannelTimeout;
                    o.Backchannel = options.Backchannel;
                    o.AllowExternalReturnUrls = options.AllowExternalReturnUrls;
                    o.CookieProtectionKey = options.CookieProtectionKey?.ToArray();
                    o.PrincipalFactory = context => {
                        HttpPrincipal principal = options.ModulePrincipalFactory?.Invoke(new OpenIDModuleContext {
                            Session = context.Session,
                            ProviderName = context.ProviderName,
                            Provider = context.Provider,
                            ClaimsPrincipal = context.ClaimsPrincipal,
                            AuthenticatedAt = context.AuthenticatedAt,
                            BasePath = basePath,
                            LoginPath = loginPath,
                            CallbackPath = callbackPath,
                            LogoutPath = logoutPath
                        }) ?? options.PrincipalFactory(context);

                        return EnsureProviderProperty(principal, context.ProviderName);
                    };

                    foreach (KeyValuePair<string, OpenIDProviderOptions> pair in options.Providers) {
                        OpenIDProviderOptions provider = pair.Value;
                        o.Add(pair.Key, clone => CopyProviderOptions(provider, clone));
                    }
                });
            }

            private static void CopyProviderOptions(OpenIDProviderOptions source, OpenIDProviderOptions destination) {
                destination.Authority = source.Authority;
                destination.MetadataAddress = source.MetadataAddress;
                destination.ClientId = source.ClientId;
                destination.ClientSecret = source.ClientSecret;
                destination.RedirectUri = source.RedirectUri;
                destination.Scopes = source.Scopes.ToArray();
                destination.AuthorizationParameters = new Dictionary<string, string>(source.AuthorizationParameters, StringComparer.Ordinal);
                destination.UsePkce = source.UsePkce;
                destination.ValidateNonce = source.ValidateNonce;
                destination.RequireHttpsMetadata = source.RequireHttpsMetadata;
                destination.ValidateIssuer = source.ValidateIssuer;
                destination.ValidIssuer = source.ValidIssuer;
                destination.UseClientSecretBasicAuthentication = source.UseClientSecretBasicAuthentication;
                destination.ClockSkew = source.ClockSkew;
                destination.NameClaimType = source.NameClaimType;
                destination.RoleClaimType = source.RoleClaimType;
                destination.RoleClaimTypes = source.RoleClaimTypes.ToArray();
                destination.ConfigureTokenValidation = source.ConfigureTokenValidation;
            }

            private static HttpPrincipal EnsureProviderProperty(HttpPrincipal principal, string providerName) {
                if (principal.Has("provider", providerName)) {
                    return principal;
                }

                List<IdentityProperty> properties = principal.Identity.Properties
                    .Where(static p => !string.Equals(p.Key, "provider", StringComparison.Ordinal))
                    .ToList();

                properties.Add(new IdentityProperty("provider", providerName));

                string? authenticationType = string.IsNullOrWhiteSpace(principal.Identity.AuthenticationType)
                    ? $"OpenID:{providerName}"
                    : principal.Identity.AuthenticationType;

                return new HttpPrincipal(new HttpIdentity(
                    isAuthenticated: principal.Identity.IsAuthenticated,
                    authenticationType: authenticationType,
                    identifier: principal.Identity.Identifier,
                    name: principal.Identity.Name,
                    email: principal.Identity.Email,
                    roles: principal.Identity.Roles,
                    properties: properties
                ));
            }

        }

        #endregion registry / configuration

        /// <summary>
        /// Module wrapper that delegates all OpenID mechanics to OpenIDHelper.
        /// </summary>
        private sealed class OpenIDModule : IHttpModule {

            /// <summary>
            /// State
            /// </summary>
            private readonly ModuleState _state;

            /// <summary>
            /// LoginPath
            /// </summary>
            private readonly string _loginPath;

            /// <summary>
            /// CallbackPath
            /// </summary>
            private readonly string _callbackPath;

            /// <summary>
            /// LogoutPath
            /// </summary>
            private readonly string _logoutPath;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="state"></param>
            /// <param name="loginPath"></param>
            /// <param name="callbackPath"></param>
            /// <param name="logoutPath"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public OpenIDModule(ModuleState state, string loginPath, string callbackPath, string logoutPath) {
                _state = state ?? throw new ArgumentNullException(nameof(state));
                _loginPath = loginPath ?? throw new ArgumentNullException(nameof(loginPath));
                _callbackPath = callbackPath ?? throw new ArgumentNullException(nameof(callbackPath));
                _logoutPath = logoutPath ?? throw new ArgumentNullException(nameof(logoutPath));
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("OpenIDModule must be installed before server start.");
                }

                server.UseMiddleware((session, next) => MiddlewareAsync(session, next, _state));
                server.MapGet(_loginPath, (HttpSession session, string provider) => LoginHandlerAsync(session, provider));
                server.MapGet(_callbackPath, (HttpSession session, string provider) => CallbackHandlerAsync(session, provider));
                server.MapGet(_logoutPath, (HttpSession session) => LogoutHandler(session));
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

                if (!configuration.AutoChallenge || session.Metadata.Has<AllowAnonymousAttribute>()) {
                    await next().ConfigureAwait(false);
                    return;
                }

                OpenIDAuthAttribute? auth = session.Metadata.Get<OpenIDAuthAttribute>();
                if (auth == null) {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (IsAuthorizedForProvider(session.Principal, auth.Provider)) {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (!configuration.Helper.HasProvider(auth.Provider)) {
                    _log.Warn($"OpenID provider '{auth.Provider}' is not configured for automatic challenge.");
                    await session.Response
                                 .Status(500)
                                 .Json(new {
                                     ok = false,
                                     error = "openid_provider_not_configured",
                                     provider = auth.Provider
                                 })
                                 .SendAsync()
                                 .ConfigureAwait(false);
                    return;
                }

                try {
                    string challengeUrl = await configuration.Helper.CreateChallengeUrlAsync(
                                                session,
                                                auth.Provider,
                                                returnUrl: CreateCurrentReturnUrl(session)
                                            ).ConfigureAwait(false);

                    await session.Response.Redirect(challengeUrl).SendAsync().ConfigureAwait(false);
                }
                catch (Exception ex) {
                    _log.Warn($"OpenID automatic challenge failed for provider '{auth.Provider}'", ex);
                    await session.Response
                                 .Status(502)
                                 .Json(new {
                                     ok = false,
                                     error = "openid_challenge_failed",
                                     provider = auth.Provider
                                 })
                                 .SendAsync()
                                 .ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Login handler
            /// </summary>
            /// <param name="session"></param>
            /// <param name="provider"></param>
            /// <returns></returns>
            private async ValueTask<object> LoginHandlerAsync(HttpSession session, string provider) {
                ModuleConfiguration configuration = _state.Snapshot ?? throw new InvalidOperationException("OpenID module configuration is missing.");

                if (!configuration.Helper.HasProvider(provider)) {
                    return session.Response.Status(400).Json(new {
                        ok = false,
                        error = "invalid_provider",
                        provider
                    });
                }

                try {
                    string challengeUrl = await configuration.Helper.CreateChallengeUrlAsync(
                                                session,
                                                provider,
                                                returnUrl: null,
                                                extraParameters: BuildChallengeParameters(session)
                                            ).ConfigureAwait(false);

                    return session.Response.Redirect(challengeUrl);
                }
                catch (Exception ex) {
                    _log.Warn($"OpenID login failed for provider '{provider}'", ex);
                    return session.Response.Status(502).Json(new {
                        ok = false,
                        error = "openid_challenge_failed",
                        provider
                    });
                }
            }

            /// <summary>
            /// Callback handler
            /// </summary>
            /// <param name="session"></param>
            /// <param name="provider"></param>
            /// <returns></returns>
            private async ValueTask<object> CallbackHandlerAsync(HttpSession session, string provider) {
                ModuleConfiguration configuration = _state.Snapshot ?? throw new InvalidOperationException("OpenID module configuration is missing.");

                OpenIDCallbackResult result = await configuration.Helper.CompleteCallbackAsync(session, provider).ConfigureAwait(false);
                if (!result.IsSuccess) {
                    return session.Response.Status(result.StatusCode).Json(new {
                        ok = false,
                        provider = result.Provider,
                        error = result.Error,
                        returnUrl = result.ReturnUrl
                    });
                }

                session.Principal = result.Principal!;
                return session.Response.Redirect(result.ReturnUrl);
            }

            /// <summary>
            /// Logout handler
            /// </summary>
            /// <param name="session"></param>
            /// <returns></returns>
            private object LogoutHandler(HttpSession session) {
                ModuleConfiguration configuration = _state.Snapshot ?? throw new InvalidOperationException("OpenID module configuration is missing.");

                string requestedReturnUrl = session.Request.Query.TryGetValue("returnUrl", out string? returnUrl) && !string.IsNullOrWhiteSpace(returnUrl)
                                                ? returnUrl
                                                : "/";

                string normalizedReturnUrl = configuration.Helper.SignOut(session, requestedReturnUrl);
                return session.Response.Redirect(normalizedReturnUrl);
            }

            private static bool IsAuthorizedForProvider(HttpPrincipal principal, string provider) {
                if (!principal.IsAuthenticated) {
                    return false;
                }

                if (principal.Has("provider", provider)) {
                    return true;
                }

                return string.Equals(principal.Identity.AuthenticationType, $"OpenID:{provider}", StringComparison.OrdinalIgnoreCase);
            }

            private static string CreateCurrentReturnUrl(HttpSession session) {
                return string.IsNullOrWhiteSpace(session.Request.RawTarget) ? session.Request.Path : session.Request.RawTarget;
            }

            private static IReadOnlyDictionary<string, string>? BuildChallengeParameters(HttpSession session) {
                Dictionary<string, string>? parameters = null;
                foreach (KeyValuePair<string, string> pair in session.Request.Query) {
                    if (string.IsNullOrWhiteSpace(pair.Key)
                        || string.IsNullOrWhiteSpace(pair.Value)
                        || string.Equals(pair.Key, "returnUrl", StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    parameters ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    parameters[pair.Key] = pair.Value;
                }

                return parameters;
            }

        }

        private static string BuildRoutePath(string basePath, string suffix) {
            return basePath == "/" ? "/" + suffix : $"{basePath}/{suffix}";
        }

    }

    /// <summary>
    /// Declares that a handler requires OpenID authentication with the given provider.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class OpenIDAuthAttribute : Attribute, IHandlerMetadata {

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="provider"></param>
        /// <exception cref="ArgumentException"></exception>
        public OpenIDAuthAttribute(string provider) {
            if (string.IsNullOrWhiteSpace(provider)) {
                throw new ArgumentException("OpenID provider must not be null or empty.", nameof(provider));
            }

            Provider = provider.Trim();
        }

        /// <summary>
        /// Provider logical name used by the module challenge flow.
        /// </summary>
        public string Provider { get; }

    }

}
