using System.Runtime.CompilerServices;
using SimpleW.Helper.Jwt;
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Jwt {

    /// <summary>
    /// JWT module extensions for SimpleW.
    /// </summary>
    public static class JwtModuleExtension {

        private static readonly ConditionalWeakTable<SimpleWServer, ModuleState> _states = new();

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<JwtModule>();

        private static readonly JwtBearerOptions _defaultHelperOptions = new();
        private static readonly Func<JwtPrincipalContext, HttpPrincipal> _defaultHelperPrincipalFactory = _defaultHelperOptions.PrincipalFactory;

        /// <summary>
        /// Adds or updates the JWT convenience module on the current server.
        /// The module is a thin wrapper over JwtBearerHelper that restores the principal
        /// and wires handler-metadata based authorization behavior.
        /// </summary>
        public static SimpleWServer UseJwtModule(this SimpleWServer server, Action<JwtModuleOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            JwtModuleOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            ModuleState state = _states.GetValue(server, static _ => new ModuleState());
            state.SetConfiguration(ModuleConfiguration.FromOptions(options));

            EnsureInstalled(server, state);
            _log.Info($"installed with login url {state.Snapshot!.LoginUrl ?? "<none>"}");
            return server;
        }

        private static void EnsureInstalled(SimpleWServer server, ModuleState state) {
            lock (state.SyncRoot) {
                if (state.IsInstalled) {
                    return;
                }

                state.IsInstalled = true;
            }

            server.UseModule(new JwtModule(state));
        }

        /// <summary>
        /// Module-level JWT configuration.
        /// Helper options stay available because the module delegates all JWT mechanics to JwtBearerHelper.
        /// </summary>
        public sealed class JwtModuleOptions : JwtBearerOptions {

            /// <summary>
            /// Optional login URL used for anonymous requests that target protected handlers.
            /// When null, the module returns a plain 401 response instead of redirecting.
            /// </summary>
            public string? LoginUrl { get; set; }

            /// <summary>
            /// When true, the module restores session.Principal from the Authorization header on every request.
            /// </summary>
            public bool RestorePrincipal { get; set; } = true;

            /// <summary>
            /// When true, handlers decorated with JwtAuthAttribute or RequireRoleAttribute are automatically enforced.
            /// </summary>
            public bool AutoAuthorize { get; set; } = true;

            /// <summary>
            /// Query-string parameter used to propagate the current return URL to LoginUrl.
            /// Set to null or empty to disable return URL propagation.
            /// </summary>
            public string? ReturnUrlParameterName { get; set; } = "returnUrl";

            /// <summary>
            /// Optional pre-built helper.
            /// When set, inline helper properties cannot be used.
            /// </summary>
            public JwtBearerHelper? Helper { get; set; }

            /// <summary>
            /// Optional module-level principal factory.
            /// It receives the module login settings in addition to the validated JWT payload.
            /// Ignored when Helper is set.
            /// </summary>
            public Func<JwtModuleContext, HttpPrincipal>? ModulePrincipalFactory { get; set; }

            /// <summary>
            /// Validate and normalize the module options.
            /// </summary>
            /// <returns></returns>
            public new JwtModuleOptions ValidateAndNormalize() {
                LoginUrl = string.IsNullOrWhiteSpace(LoginUrl) ? null : LoginUrl.Trim();
                ReturnUrlParameterName = string.IsNullOrWhiteSpace(ReturnUrlParameterName) ? null : ReturnUrlParameterName.Trim();

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
                bool hasInlineHelperConfig = !string.IsNullOrWhiteSpace(SecretKey)
                                             || !string.Equals(ExpectedIssuer, _defaultHelperOptions.ExpectedIssuer, StringComparison.Ordinal)
                                             || !string.Equals(ExpectedAudience, _defaultHelperOptions.ExpectedAudience, StringComparison.Ordinal)
                                             || ClockSkew != _defaultHelperOptions.ClockSkew
                                             || !string.Equals(Algorithm, _defaultHelperOptions.Algorithm, StringComparison.Ordinal)
                                             || !string.Equals(Scheme, _defaultHelperOptions.Scheme, StringComparison.Ordinal)
                                             || !string.Equals(AuthenticationType, _defaultHelperOptions.AuthenticationType, StringComparison.Ordinal)
                                             || !Equals(PrincipalFactory, _defaultHelperPrincipalFactory);

                if (hasInlineHelperConfig || ModulePrincipalFactory != null) {
                    throw new ArgumentException("JwtModuleOptions cannot be combined with inline helper settings, PrincipalFactory, or ModulePrincipalFactory when Helper is provided.");
                }
            }

        }

        /// <summary>
        /// Additional context exposed by the module-level principal factory.
        /// </summary>
        public sealed class JwtModuleContext {

            /// <summary>
            /// Current session.
            /// </summary>
            public required HttpSession Session { get; init; }

            /// <summary>
            /// Validated JWT token string.
            /// </summary>
            public required string Token { get; init; }

            /// <summary>
            /// Subject (sub) claim.
            /// </summary>
            public required string? Subject { get; init; }

            /// <summary>
            /// Name claim.
            /// </summary>
            public required string? Name { get; init; }

            /// <summary>
            /// Email claim.
            /// </summary>
            public required string? Email { get; init; }

            /// <summary>
            /// Issuer (iss) claim.
            /// </summary>
            public required string? Issuer { get; init; }

            /// <summary>
            /// Audience (aud) claim values.
            /// </summary>
            public required string[] Audiences { get; init; }

            /// <summary>
            /// Roles extracted from role/roles claims.
            /// </summary>
            public required string[] Roles { get; init; }

            /// <summary>
            /// Extra JWT claims mapped to SimpleW identity properties.
            /// </summary>
            public required IReadOnlyList<IdentityProperty> Properties { get; init; }

            /// <summary>
            /// Authentication time in UTC.
            /// </summary>
            public required DateTimeOffset AuthenticatedAt { get; init; }

            /// <summary>
            /// Authentication type used for the rebuilt identity.
            /// </summary>
            public required string AuthenticationType { get; init; }

            /// <summary>
            /// Authorization scheme used for the HTTP authentication flow.
            /// </summary>
            public required string Scheme { get; init; }

            /// <summary>
            /// Login URL used by the module when redirecting anonymous requests.
            /// </summary>
            public required string? LoginUrl { get; init; }

            /// <summary>
            /// Query-string parameter used to propagate the return URL to LoginUrl.
            /// </summary>
            public required string? ReturnUrlParameterName { get; init; }

        }

        #region registry / configuration

        /// <summary>
        /// Per-server JWT module state.
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
            string? LoginUrl,
            string? ReturnUrlParameterName,
            bool RestorePrincipal,
            bool AutoAuthorize,
            JwtBearerHelper Helper
        ) {

            public static ModuleConfiguration FromOptions(JwtModuleOptions options) {
                return new ModuleConfiguration(
                    LoginUrl: options.LoginUrl,
                    ReturnUrlParameterName: options.ReturnUrlParameterName,
                    RestorePrincipal: options.RestorePrincipal,
                    AutoAuthorize: options.AutoAuthorize,
                    Helper: options.Helper ?? CreateHelper(options)
                );
            }

            private static JwtBearerHelper CreateHelper(JwtModuleOptions options) {
                return new JwtBearerHelper(o => {
                    o.SecretKey = options.SecretKey;
                    o.ExpectedIssuer = options.ExpectedIssuer;
                    o.ExpectedAudience = options.ExpectedAudience;
                    o.ClockSkew = options.ClockSkew;
                    o.Algorithm = options.Algorithm;
                    o.Scheme = options.Scheme;
                    o.AuthenticationType = options.AuthenticationType;
                    o.PrincipalFactory = context => options.ModulePrincipalFactory?.Invoke(new JwtModuleContext {
                        Session = context.Session ?? throw new InvalidOperationException("JWT module principal factory requires an HTTP session."),
                        Token = context.Token,
                        Subject = context.Subject,
                        Name = context.Name,
                        Email = context.Email,
                        Issuer = context.Issuer,
                        Audiences = context.Audiences.ToArray(),
                        Roles = context.Roles.ToArray(),
                        Properties = context.Properties.ToArray(),
                        AuthenticatedAt = context.AuthenticatedAt,
                        AuthenticationType = context.AuthenticationType,
                        Scheme = context.Scheme,
                        LoginUrl = options.LoginUrl,
                        ReturnUrlParameterName = options.ReturnUrlParameterName
                    }) ?? options.PrincipalFactory(context);
                });
            }

        }

        #endregion registry / configuration

        /// <summary>
        /// Module wrapper that delegates all JWT mechanics to JwtBearerHelper.
        /// </summary>
        private sealed class JwtModule : IHttpModule {

            /// <summary>
            /// State
            /// </summary>
            private readonly ModuleState _state;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="state"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public JwtModule(ModuleState state) {
                _state = state ?? throw new ArgumentNullException(nameof(state));
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("JwtModule must be installed before server start.");
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

                JwtAuthAttribute? auth = session.Metadata.Get<JwtAuthAttribute>();
                IReadOnlyList<RequireRoleAttribute> requiredRoles = session.Metadata.GetAll<RequireRoleAttribute>();

                if (auth == null && requiredRoles.Count == 0) {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (!session.Principal.IsAuthenticated) {
                    await HandleAnonymousAsync(session, configuration).ConfigureAwait(false);
                    return;
                }

                RequireRoleAttribute? failedRole = FindFailedRole(session.Principal, requiredRoles);
                if (failedRole != null) {
                    await session.Response
                                 .Status(403)
                                 .Json(new {
                                     ok = false,
                                     error = "forbidden",
                                     role = failedRole.Role
                                 })
                                 .SendAsync()
                                 .ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            }

            private static RequireRoleAttribute? FindFailedRole(HttpPrincipal principal, IReadOnlyList<RequireRoleAttribute> requiredRoles) {
                foreach (RequireRoleAttribute requirement in requiredRoles) {
                    if (!principal.IsInRoles(requirement.Role)) {
                        return requirement;
                    }
                }

                return null;
            }

            private static ValueTask HandleAnonymousAsync(HttpSession session, ModuleConfiguration configuration) {
                if (!string.IsNullOrWhiteSpace(configuration.LoginUrl)) {
                    string redirectUrl = BuildLoginRedirectUrl(
                        configuration.LoginUrl,
                        CreateCurrentReturnUrl(session),
                        configuration.ReturnUrlParameterName
                    );

                    return session.Response.Redirect(redirectUrl).SendAsync();
                }

                return session.Response.Unauthorized().SendAsync();
            }

        }

        private static string CreateCurrentReturnUrl(HttpSession session) {
            return string.IsNullOrWhiteSpace(session.Request.RawTarget) ? session.Request.Path : session.Request.RawTarget;
        }

        private static string BuildLoginRedirectUrl(string loginUrl, string returnUrl, string? returnUrlParameterName) {
            if (string.IsNullOrWhiteSpace(returnUrlParameterName)) {
                return loginUrl;
            }

            int fragmentIndex = loginUrl.IndexOf('#');
            string fragment = fragmentIndex >= 0 ? loginUrl[fragmentIndex..] : string.Empty;
            string loginUrlWithoutFragment = fragmentIndex >= 0 ? loginUrl[..fragmentIndex] : loginUrl;

            string separator = loginUrlWithoutFragment.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{loginUrlWithoutFragment}{separator}{Uri.EscapeDataString(returnUrlParameterName)}={Uri.EscapeDataString(returnUrl)}{fragment}";
        }

    }

    /// <summary>
    /// Declares that a handler requires an authenticated principal.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class JwtAuthAttribute : Attribute, IHandlerMetadata {
    }

}
