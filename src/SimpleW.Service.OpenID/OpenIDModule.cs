using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SimpleW.Modules;


namespace SimpleW.Service.OpenID {

    /// <summary>
    /// OpenIDModuleExtension
    /// </summary>
    public static class OpenIDModuleExtension {

        /// <summary>
        /// Use OpenID Module
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseOpenIDModule(this SimpleWServer server, Action<OpenIDMultiOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            var multi = new OpenIDMultiOptions();
            configure?.Invoke(multi);
            multi.Validate();

            server.UseModule(new OpenIDModule(multi));
            return server;
        }

        /// <summary>
        /// Multi configuration (providers + defaults)
        /// </summary>
        public sealed class OpenIDMultiOptions {

            /// <summary>
            /// Providers
            /// </summary>
            private readonly Dictionary<string, OpenIDOptions> _providers = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Providers
            /// </summary>
            public IReadOnlyDictionary<string, OpenIDOptions> Providers => _providers;

            /// <summary>
            /// Path prefix for login or callback routes
            /// Final routes:
            ///   - {BasePath}/login/:provider
            ///   - {BasePath}/callback/:provider
            /// </summary>
            public string BasePath { get; set; } = "/auth/oidc";

            /// <summary>
            /// Shared cookie name for all providers (recommended)
            /// </summary>
            public string CookieName { get; set; } = "simplew_oidc";

            /// <summary>
            /// Indicates whether the authentication cookie should be marked
            /// as Secure. When true, the cookie is only sent over HTTPS.
            /// </summary>
            public bool CookieSecure { get; set; } = true;

            /// <summary>
            /// SameSite policy applied to the authentication cookie.
            /// Lax is a good default for OpenID flows, allowing redirects
            /// from the identity provider while still offering CSRF protection.
            /// </summary>
            public HttpResponse.SameSiteMode CookieSameSite { get; set; } = HttpResponse.SameSiteMode.Lax;

            /// <summary>
            /// Add a provider configuration
            /// </summary>
            public void Add(string provider, Action<OpenIDOptions> configure) {
                if (string.IsNullOrWhiteSpace(provider)) {
                    throw new ArgumentException("Provider name is required.", nameof(provider));
                }

                OpenIDOptions o = new();
                configure?.Invoke(o);
                _providers[provider] = o;
            }

            /// <summary>
            /// Check Properties and return
            /// </summary>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            public OpenIDMultiOptions Validate() {
                if (_providers.Count == 0) {
                    throw new ArgumentException("At least one OpenID provider must be configured.");
                }

                foreach (var kv in _providers) {
                    kv.Value.Validate();
                }

                if (!BasePath.StartsWith("/", StringComparison.Ordinal)) {
                    throw new ArgumentException("BasePath must start with '/'.");
                }

                return this;
            }

        }

        /// <summary>
        /// Provider options (same as your current OpenIDOptions, but WITHOUT fixed Login/Callback paths)
        /// </summary>
        public sealed class OpenIDOptions {

            /// <summary>
            /// Provider authority
            /// ex: https://accounts.google.com, https://login.microsoftonline.com/{tenant}/v2.0
            /// </summary>
            public string Authority { get; set; } = string.Empty;

            /// <summary>
            /// CliendId
            /// </summary>
            public string ClientId { get; set; } = string.Empty;

            /// <summary>
            /// ClientSecret
            /// </summary>
            public string ClientSecret { get; set; } = string.Empty;

            /// <summary>
            /// Either set RedirectUri explicitly (recommended), OR set PublicBaseUrl and we append CallbackPath.
            /// </summary>
            public string? RedirectUri { get; set; }

            /// <summary>
            /// Public URL of your app, e.g. https://myapp.example.com
            /// Used only if RedirectUri is not set.
            /// </summary>
            public string? PublicBaseUrl { get; set; }

            /// <summary>
            /// OpenID Connect scopes requested during authentication.
            /// "openid" is mandatory, while "profile" and "email"
            /// allow access to basic user information.
            /// </summary>
            public string[] Scopes { get; set; } = ["openid", "profile", "email"];

            /// <summary>
            /// Indicates whether HTTPS is required when retrieving
            /// OpenID Connect metadata (discovery document).
            /// Should always be true in production.
            /// </summary>
            public bool RequireHttpsMetadata { get; set; } = true;

            /// <summary>
            /// Time-to-live (in minutes) for the temporary authentication state.
            /// This value limits how long a login attempt (state + nonce)
            /// is considered valid, protecting against replay attacks.
            /// </summary>
            public int StateTtlMinutes { get; set; } = 10;

            /// <summary>
            /// Default lifetime (in minutes) of an authenticated user session
            /// when the identity provider does not provide an explicit expiration.
            /// </summary>
            public int SessionTtlMinutes { get; set; } = 8 * 60;

            /// <summary>
            /// Allowed clock skew (in seconds) when validating token timestamps
            /// (nbf, exp). This compensates small time differences between
            /// the server and the identity provider.
            /// </summary>
            public int ClockSkewSeconds { get; set; } = 60;

            /// <summary>
            /// Number of incoming requests between cleanup passes.
            /// Cleanup removes expired authentication states and sessions
            /// without using background timers.
            /// </summary>
            public int CleanupEveryNRequests { get; set; } = 256;

            /// <summary>
            /// Store provider tokens in-memory in the session.
            /// If false: we still store claims + subject.
            /// </summary>
            public bool SaveTokens { get; set; } = false;

            /// <summary>
            /// OIDC prompt parameter (optional), e.g. "select_account"
            /// </summary>
            public string? Prompt { get; set; }

            /// <summary>
            /// Set to enable login_hint query string support.
            /// </summary>
            public string? LoginHint { get; set; }

            /// <summary>
            /// ClientAuthentication
            /// </summary>
            public ClientAuthMode ClientAuthentication { get; set; } = ClientAuthMode.Basic;

            /// <summary>
            /// HttpClient
            /// </summary>
            public HttpClient? HttpClient { get; set; }

            /// <summary>
            /// Map an AuthSession to SimpleW.IWebUser implementation.
            /// </summary>
            public Func<AuthSession, IWebUser> UserFactory { get; set; } = (auth) => {

                string? Claim(string type) => auth.Claims.FirstOrDefault(c => c.Type == type)?.Value;

                string[] ClaimsArray(string type) => auth.Claims
                                                         .Where(c => c.Type == type)
                                                         .Select(c => c.Value)
                                                         .Distinct(StringComparer.OrdinalIgnoreCase)
                                                         .ToArray();

                // stable Guid from OIDC "sub"
                static Guid GuidFromSub(string sub) {
                    using var sha = SHA256.Create();
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sub));
                    Span<byte> g = stackalloc byte[16];
                    hash.AsSpan(0, 16).CopyTo(g);
                    return new Guid(g);
                }

                string sub = Claim("sub") ?? Guid.NewGuid().ToString();

                var user = auth.Tokens != null ? new TokenWebUser() : new WebUser();
                user.Identity = true;
                user.Id = GuidFromSub(sub);
                user.Login = Claim("preferred_username") ?? Claim("email") ?? sub;
                user.Mail = Claim("email");
                user.FullName = Claim("name");
                user.Profile = Claim("iss"); // provider
                user.Roles = ClaimsArray("roles")
                                .Concat(ClaimsArray("role"))
                                .Concat(ClaimsArray("groups"))
                                .Distinct()
                                .ToArray();

                if (user is TokenWebUser twu && auth.Tokens?.IdToken != null) {
                    twu.Token = auth.Tokens.IdToken;
                    twu.Refresh = false;
                }

                return user;
            };

            /// <summary>
            /// Check Properties and return
            /// </summary>
            /// <exception cref="ArgumentException"></exception>
            public OpenIDOptions Validate() {
                if (string.IsNullOrWhiteSpace(Authority)) {
                    throw new ArgumentException("Authority is required.");
                }
                if (string.IsNullOrWhiteSpace(ClientId)) {
                    throw new ArgumentException("ClientId is required.");
                }
                if (string.IsNullOrWhiteSpace(ClientSecret)) {
                    throw new ArgumentException("ClientSecret is required.");
                }
                if (string.IsNullOrWhiteSpace(RedirectUri) && string.IsNullOrWhiteSpace(PublicBaseUrl)) {
                    throw new ArgumentException("Either RedirectUri or PublicBaseUrl must be set.");
                }

                return this;
            }

        }

        /// <summary>
        /// Internal OpenID module (multi-provider)
        /// </summary>
        private sealed class OpenIDModule : IHttpModule {

            /// <summary>
            /// Options
            /// </summary>
            private readonly OpenIDMultiOptions _multi;

            /// <summary>
            /// per provider runtime objects
            /// </summary>
            private readonly ConcurrentDictionary<string, ProviderRuntime> _runtime = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Pending
            /// </summary>
            private readonly ConcurrentDictionary<string, PendingAuth> _pending = new(StringComparer.Ordinal);

            /// <summary>
            /// AuthSession
            /// </summary>
            private readonly ConcurrentDictionary<string, AuthSession> _sessions = new(StringComparer.Ordinal);

            /// <summary>
            /// CleanUpTick
            /// </summary>
            private long _cleanupTick;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="multi"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public OpenIDModule(OpenIDMultiOptions multi) {
                _multi = multi ?? throw new ArgumentNullException(nameof(multi));
                _multi.Validate();
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            public void Install(SimpleWServer server) {
                if (server.IsStarted) {
                    throw new InvalidOperationException("OpenIDModule must be installed before server start.");
                }

                // register middleware: restore user from cookie
                server.Router.UseMiddleware(async (session, next) => {
                    try {
                        MaybeCleanup();

                        if (session.Request.Headers.TryGetCookie(_multi.CookieName, out var sid)
                            && !string.IsNullOrWhiteSpace(sid)
                            && _sessions.TryGetValue(sid!, out var auth)
                            && auth.ExpiresUtc > DateTimeOffset.UtcNow
                        ) {
                            // pick provider from stored session
                            var opt = GetRuntime(auth.Provider).Options;
                            session.Request.User = opt.UserFactory(auth);
                        }
                    }
                    catch {
                        // ignore auth restore errors (don’t break request pipeline)
                    }

                    await next();
                });

                // routes:
                // /auth/oidc/login/{provider}
                // /auth/oidc/callback/{provider}
                // plus optional default shortcuts:
                // /auth/oidc/login (=> default provider)
                // /auth/oidc/callback (rarely used, but we can support if you really want)
                string basePath = _multi.BasePath.TrimEnd('/');

                server.Router.MapGet($"{basePath}/login/:provider", (HttpSession session, string provider) => LoginHandler(session, provider));
                server.Router.MapGet($"{basePath}/callback/:provider", (HttpSession session, string provider) => CallbackHandler(session, provider));
                server.Router.MapGet($"{basePath}/logout", (HttpSession s) => LogoutHandler(s));
            }

            /// <summary>
            /// GetRuntime
            /// </summary>
            /// <param name="provider"></param>
            /// <returns></returns>
            /// <exception cref="InvalidOperationException"></exception>
            private ProviderRuntime GetRuntime(string provider) {
                if (!_multi.Providers.TryGetValue(provider, out var opt)) {
                    throw new InvalidOperationException($"Unknown OpenID provider '{provider}'.");
                }

                return _runtime.GetOrAdd(provider, _ => {
                    HttpClient http = opt.HttpClient ?? new(new SocketsHttpHandler {
                        AutomaticDecompression = System.Net.DecompressionMethods.All
                    });

                    HttpDocumentRetriever retriever = new(http) { RequireHttps = opt.RequireHttpsMetadata };
                    ConfigurationManager<OpenIdConnectConfiguration> cfg = new(
                        $"{opt.Authority.TrimEnd('/')}/.well-known/openid-configuration",
                        new OpenIdConnectConfigurationRetriever(),
                        retriever
                    );

                    return new ProviderRuntime(provider, opt, http, cfg);
                });
            }

            /// <summary>
            /// LoginHandler
            /// </summary>
            /// <param name="session"></param>
            /// <param name="provider"></param>
            /// <returns></returns>
            private async ValueTask LoginHandler(HttpSession session, string provider) {
                if (string.IsNullOrWhiteSpace(provider)) {
                    throw new InvalidOperationException("Missing provider route value.");
                }

                var rt = GetRuntime(provider);
                var opt = rt.Options;

                string? returnUrl = null;
                session.Request.Query?.TryGetValue("returnUrl", out returnUrl);
                returnUrl = NormalizeReturnUrl(returnUrl);

                string state = Base64Url(RandomBytes(32));
                string nonce = Base64Url(RandomBytes(32));

                _pending[state] = new PendingAuth(
                    Provider: provider,
                    Nonce: nonce,
                    ReturnUrl: returnUrl,
                    ExpiresUtc: DateTimeOffset.UtcNow.AddMinutes(opt.StateTtlMinutes)
                );

                var cfg = await rt.CfgManager.GetConfigurationAsync(session.RequestAborted).ConfigureAwait(false);

                string redirectUri = BuildRedirectUri(rt, provider);

                StringBuilder authorize = new();
                authorize.Append(cfg.AuthorizationEndpoint);
                authorize.Append(cfg.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?");

                authorize.Append("client_id=").Append(UrlEncode(opt.ClientId));
                authorize.Append("&response_type=code");
                authorize.Append("&redirect_uri=").Append(UrlEncode(redirectUri));
                authorize.Append("&scope=").Append(UrlEncode(string.Join(' ', opt.Scopes)));
                authorize.Append("&state=").Append(UrlEncode(state));
                authorize.Append("&nonce=").Append(UrlEncode(nonce));

                if (!string.IsNullOrWhiteSpace(opt.Prompt)) {
                    authorize.Append("&prompt=").Append(UrlEncode(opt.Prompt!));
                }

                if (!string.IsNullOrWhiteSpace(opt.LoginHint) && session.Request.Query?.TryGetValue("loginHint", out var lh) == true) {
                    authorize.Append("&login_hint=").Append(UrlEncode(lh));
                }

                await session.Response
                             .Redirect(authorize.ToString())
                             .SendAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// CallbackHandler
            /// </summary>
            /// <param name="session"></param>
            /// <param name="provider"></param>
            /// <returns></returns>
            private async ValueTask CallbackHandler(HttpSession session, string provider) {
                if (string.IsNullOrWhiteSpace(provider)) {
                    throw new InvalidOperationException("Missing provider route value.");
                }
                var rt = GetRuntime(provider);
                var opt = rt.Options;

                if (session.Request.Query == null
                    || !session.Request.Query.TryGetValue("code", out var code)
                    || !session.Request.Query.TryGetValue("state", out var state)
                    || string.IsNullOrWhiteSpace(code)
                    || string.IsNullOrWhiteSpace(state)
                ) {
                    await Fail(session, 400, "Missing code/state").ConfigureAwait(false);
                    return;
                }

                if (!_pending.TryRemove(state, out var pending) || pending.ExpiresUtc <= DateTimeOffset.UtcNow) {
                    await Fail(session, 400, "Invalid/expired state").ConfigureAwait(false);
                    return;
                }

                if (!string.Equals(pending.Provider, provider, StringComparison.OrdinalIgnoreCase)) {
                    await Fail(session, 400, "State/provider mismatch").ConfigureAwait(false);
                    return;
                }

                var cfg = await rt.CfgManager.GetConfigurationAsync(session.RequestAborted).ConfigureAwait(false);
                string redirectUri = BuildRedirectUri(rt, provider);

                var token = await ExchangeCodeAsync(rt.Http, opt, cfg.TokenEndpoint, code, redirectUri, session.RequestAborted).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token.IdToken)) {
                    await Fail(session, 401, "Missing id_token").ConfigureAwait(false);
                    return;
                }

                var principal = ValidateIdToken(opt, token.IdToken!, cfg, pending.Nonce);

                var sid = Base64Url(RandomBytes(32));
                var now = DateTimeOffset.UtcNow;

                var expires = token.ExpiresInSeconds > 0 ? now.AddSeconds(token.ExpiresInSeconds) : now.AddMinutes(opt.SessionTtlMinutes);

                AuthSession authSession = new(
                    SessionId: sid,
                    Provider: provider,
                    CreatedUtc: now,
                    ExpiresUtc: expires,
                    Subject: Find(principal, "sub") ?? Find(principal, ClaimTypes.NameIdentifier) ?? "",
                    Claims: principal.Claims.ToArray(),
                    Tokens: opt.SaveTokens ? token : null
                );

                _sessions[sid] = authSession;

                session.Response.SetCookie(
                    _multi.CookieName,
                    sid,
                    new HttpResponse.CookieOptions(
                        path: "/",
                        maxAgeSeconds: (int)Math.Max(60, (expires - now).TotalSeconds),
                        secure: _multi.CookieSecure,
                        httpOnly: true,
                        sameSite: _multi.CookieSameSite
                    )
                );

                await session.Response.Redirect(pending.ReturnUrl).SendAsync().ConfigureAwait(false);
            }

            /// <summary>
            /// LogoutHandler
            /// </summary>
            /// <param name="session"></param>
            /// <returns></returns>
            private async ValueTask LogoutHandler(HttpSession session) {
                string? returnUrl = null;
                session.Request.Query?.TryGetValue("returnUrl", out returnUrl);
                returnUrl = NormalizeReturnUrl(returnUrl);

                if (session.Request.Headers.TryGetCookie(_multi.CookieName, out var sid) && !string.IsNullOrWhiteSpace(sid)) {
                    _sessions.TryRemove(sid!, out _);
                }

                await session.Response.DeleteCookie(_multi.CookieName, path: "/").Redirect(returnUrl).SendAsync().ConfigureAwait(false);
            }

            #region helpers

            private static async Task<TokenResponse> ExchangeCodeAsync(HttpClient http, OpenIDOptions opt, string tokenEndpoint, string code, string redirectUri, CancellationToken ct) {
                
                // application/x-www-form-urlencoded
                Dictionary<string, string> body = new(StringComparer.Ordinal) {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = opt.ClientId,
                };

                // client_secret: either in body or Basic auth
                using var req = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint) {
                    Content = new FormUrlEncodedContent(body)
                };

                if (opt.ClientAuthentication == ClientAuthMode.Basic) {
                    var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{opt.ClientId}:{opt.ClientSecret}"));
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                }
                else {
                    // post body
                    body["client_secret"] = opt.ClientSecret;
                    req.Content = new FormUrlEncodedContent(body);
                }

                using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode) {
                    throw new InvalidOperationException($"Token endpoint error {(int)resp.StatusCode}: {json}");
                }

                // Minimal JSON parsing without bringing a JSON dependency:
                // You can replace by server.JsonEngine if you want.
                return TokenResponse.FromJson(json);
            }

            private static ClaimsPrincipal ValidateIdToken(OpenIDOptions opt, string idToken, OpenIdConnectConfiguration cfg, string expectedNonce) {
                JwtSecurityTokenHandler handler = new();

                TokenValidationParameters validation = new TokenValidationParameters {
                    ValidateIssuer = true,
                    ValidIssuer = cfg.Issuer,

                    ValidateAudience = true,
                    ValidAudience = opt.ClientId,

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(opt.ClockSkewSeconds),

                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = cfg.SigningKeys
                };

                var principal = handler.ValidateToken(idToken, validation, out _);

                // nonce check (recommended for OIDC)
                string? nonce = principal.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
                if (!string.Equals(nonce, expectedNonce, StringComparison.Ordinal)) {
                    throw new SecurityTokenException("Invalid nonce");
                }

                return principal;
            }

            private string BuildRedirectUri(ProviderRuntime rt, string provider) {
                // In multi-provider, callback includes provider in path
                if (!string.IsNullOrWhiteSpace(rt.Options.RedirectUri)) {
                    return rt.Options.RedirectUri!;
                }

                string? baseUrl = rt.Options.PublicBaseUrl?.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(baseUrl)) {
                    throw new InvalidOperationException("Either RedirectUri or PublicBaseUrl must be set.");
                }

                return $"{baseUrl}{_multi.BasePath.TrimEnd('/')}/callback/{provider}";
            }

            private static string NormalizeReturnUrl(string? returnUrl) {
                if (string.IsNullOrWhiteSpace(returnUrl)) {
                    return "/";
                }

                // only allow local paths to avoid open redirect
                if (!returnUrl.StartsWith("/", StringComparison.Ordinal) || returnUrl.StartsWith("//", StringComparison.Ordinal)) {
                    return "/";
                }

                return returnUrl;
            }

            private async ValueTask Fail(HttpSession session, int statusCode, string message) {
                session.Response.Status(statusCode).Json(new { ok = false, error = message });
                await session.Response.SendAsync().ConfigureAwait(false);
            }

            private void MaybeCleanup() {
                long tick = Interlocked.Increment(ref _cleanupTick);
                if (tick % _multi.Providers.Values.First().CleanupEveryNRequests != 0) {
                    return;
                }

                DateTimeOffset now = DateTimeOffset.UtcNow;

                foreach (var kv in _pending) {
                    if (kv.Value.ExpiresUtc <= now) {
                        _pending.TryRemove(kv.Key, out _);
                    }
                }

                foreach (var kv in _sessions) {
                    if (kv.Value.ExpiresUtc <= now) {
                        _sessions.TryRemove(kv.Key, out _);
                    }
                }
            }

            private static byte[] RandomBytes(int len) {
                byte[] bytes = new byte[len];
                RandomNumberGenerator.Fill(bytes);
                return bytes;
            }

            private static string Base64Url(byte[] bytes) => Base64UrlEncoder.Encode(bytes);

            private static string UrlEncode(string s) => Uri.EscapeDataString(s);

            static string? Find(ClaimsPrincipal p, string type) => p.Claims.FirstOrDefault(c => c.Type == type)?.Value;

            #endregion helpers

            /// <summary>
            /// ProviderRuntime
            /// </summary>
            /// <param name="Provider"></param>
            /// <param name="Options"></param>
            /// <param name="Http"></param>
            /// <param name="CfgManager"></param>
            private sealed record ProviderRuntime(string Provider, OpenIDOptions Options, HttpClient Http, ConfigurationManager<OpenIdConnectConfiguration> CfgManager);

        }

        /// <summary>
        /// ClientAuthMode
        /// </summary>
        public enum ClientAuthMode {
            /// <summary>
            /// Authorization: Basic base64(client_id:client_secret)
            /// </summary>
            Basic,
            /// <summary>
            /// client_secret in POST body (some providers accept it)
            /// </summary>
            PostBody
        }

        /// <summary>
        /// PendingAuth
        /// </summary>
        /// <param name="Provider"></param>
        /// <param name="Nonce"></param>
        /// <param name="ReturnUrl"></param>
        /// <param name="ExpiresUtc"></param>
        internal readonly record struct PendingAuth(string Provider, string Nonce, string ReturnUrl, DateTimeOffset ExpiresUtc);

        /// <summary>
        /// AuthSession
        /// </summary>
        /// <param name="SessionId"></param>
        /// <param name="Provider"></param>
        /// <param name="CreatedUtc"></param>
        /// <param name="ExpiresUtc"></param>
        /// <param name="Subject"></param>
        /// <param name="Claims"></param>
        /// <param name="Tokens"></param>
        public sealed record AuthSession(string SessionId, string Provider, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, string Subject, Claim[] Claims, TokenResponse? Tokens);

        /// <summary>
        /// TokenResponse
        /// </summary>
        /// <param name="AccessToken"></param>
        /// <param name="IdToken"></param>
        /// <param name="RefreshToken"></param>
        /// <param name="ExpiresInSeconds"></param>
        /// <param name="TokenType"></param>
        public sealed record TokenResponse(string? AccessToken, string? IdToken, string? RefreshToken, int ExpiresInSeconds, string? TokenType) {

            /// <summary>
            /// Minimal JSON parser for well-known fields.
            /// Replace with server.JsonEngine if you prefer.
            /// </summary>
            /// <param name="json"></param>
            /// <returns></returns>
            public static TokenResponse FromJson(string json) {
                static string? GetString(string j, string key) {
                    var needle = $"\"{key}\"";
                    var i = j.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) {
                        return null;
                    }
                    i = j.IndexOf(':', i);
                    if (i < 0) {
                        return null;
                    }
                    i++;

                    while (i < j.Length && char.IsWhiteSpace(j[i])) {
                        i++;
                    }

                    if (i < j.Length && j[i] == '"') {
                        i++;
                        var end = j.IndexOf('"', i);
                        if (end < 0) {
                            return null;
                        }
                        return j.Substring(i, end - i);
                    }

                    // non-string (rare)
                    var end2 = j.IndexOfAny([',', '}', '\n', '\r'], i);
                    if (end2 < 0) {
                        end2 = j.Length;
                    }
                    return j.Substring(i, end2 - i).Trim();
                }

                static int GetInt(string j, string key) {
                    var s = GetString(j, key);
                    return int.TryParse(s, out var v) ? v : 0;
                }

                return new TokenResponse(
                    AccessToken: GetString(json, "access_token"),
                    IdToken: GetString(json, "id_token"),
                    RefreshToken: GetString(json, "refresh_token"),
                    ExpiresInSeconds: GetInt(json, "expires_in"),
                    TokenType: GetString(json, "token_type")
                );
            }

        }

    }

}
