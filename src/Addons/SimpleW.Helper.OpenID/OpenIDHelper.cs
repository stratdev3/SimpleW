using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SimpleW.Observability;


namespace SimpleW.Helper.OpenID {

    /// <summary>
    /// Reusable OpenID Connect helper for SimpleW.
    /// It manages provider discovery, authorization challenges, callback completion,
    /// token validation, principal creation, and cookie-based principal restoration.
    /// Route protection stays in application middleware.
    /// </summary>
    public sealed class OpenIDHelper : IDisposable {

        #region fields

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<OpenIDHelper>();

        private readonly OpenIDHelperOptions _options;
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private readonly JwtSecurityTokenHandler _tokenHandler;
        private readonly Dictionary<string, ProviderState> _providers;
        private readonly byte[] _challengeProtectionKey;
        private bool _disposed;

        #endregion fields

        #region constructor

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="configure"></param>
        public OpenIDHelper(Action<OpenIDHelperOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            OpenIDHelperOptions options = new();
            configure(options);
            options.ValidateAndNormalize();

            _options = options;
            _httpClient = options.Backchannel ?? new HttpClient() {
                Timeout = options.BackchannelTimeout
            };
            _ownsHttpClient = options.Backchannel == null;
            _tokenHandler = new JwtSecurityTokenHandler() {
                MapInboundClaims = false
            };
            _challengeProtectionKey = options.CookieProtectionKey?.ToArray() ?? RandomNumberGenerator.GetBytes(32);

            _providers = new Dictionary<string, ProviderState>(StringComparer.OrdinalIgnoreCase);
            foreach (OpenIDProviderOptions provider in options.Providers.Values) {
                HttpDocumentRetriever documentRetriever = new(_httpClient) {
                    RequireHttps = provider.RequireHttpsMetadata
                };

                ConfigurationManager<OpenIdConnectConfiguration> configurationManager = new(
                    provider.MetadataAddress!,
                    new OpenIdConnectConfigurationRetriever(),
                    documentRetriever
                );

                _providers[provider.Name] = new ProviderState(provider, configurationManager);
            }
        }

        #endregion constructor

        #region public methods

        /// <summary>
        /// Returns true when the helper has a provider with this name.
        /// </summary>
        /// <param name="provider"></param>
        /// <returns></returns>
        public bool HasProvider(string provider) {
            return !string.IsNullOrWhiteSpace(provider) && _providers.ContainsKey(provider);
        }

        /// <summary>
        /// Create the provider authorization URL.
        /// The application usually redirects the current response to this URL.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="provider"></param>
        /// <param name="returnUrl"></param>
        /// <param name="extraParameters"></param>
        /// <returns></returns>
        public async Task<string> CreateChallengeUrlAsync(
            HttpSession session,
            string provider,
            string? returnUrl = null,
            IReadOnlyDictionary<string, string>? extraParameters = null
        ) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(session);

            ProviderState state = GetProviderState(provider);
            OpenIdConnectConfiguration configuration = await state.ConfigurationManager.GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(configuration.AuthorizationEndpoint)) {
                throw new InvalidOperationException($"OpenID provider '{state.Options.Name}' does not expose an authorization endpoint.");
            }

            string stateValue = GenerateRandomValue();
            string nonce = GenerateRandomValue();
            string? codeVerifier = state.Options.UsePkce ? GenerateRandomValue() : null;
            if (returnUrl == null && session.Request.Query.TryGetValue("returnUrl", out string? queryReturnUrl)) {
                returnUrl = queryReturnUrl;
            }
            string effectiveReturnUrl = NormalizeReturnUrl(returnUrl ?? "/", _options.AllowExternalReturnUrls);
            DateTimeOffset now = DateTimeOffset.UtcNow;

            ChallengeTicket challenge = new(
                Provider: state.Options.Name,
                State: stateValue,
                Nonce: nonce,
                CodeVerifier: codeVerifier,
                ReturnUrl: effectiveReturnUrl,
                ExpiresAt: now.Add(_options.ChallengeLifetime)
            );
            SetChallengeCookie(session, challenge);

            Dictionary<string, string> parameters = new(StringComparer.Ordinal) {
                ["client_id"] = state.Options.ClientId,
                ["response_type"] = "code",
                ["scope"] = string.Join(' ', state.Options.Scopes),
                ["redirect_uri"] = state.Options.RedirectUri,
                ["state"] = stateValue,
                ["nonce"] = nonce
            };

            foreach (KeyValuePair<string, string> pair in state.Options.AuthorizationParameters) {
                parameters[pair.Key] = pair.Value;
            }

            if (extraParameters != null) {
                foreach (KeyValuePair<string, string> pair in extraParameters) {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null) {
                        parameters[pair.Key] = pair.Value;
                    }
                }
            }

            // Security-sensitive values are restored last so caller-supplied parameters cannot replace them.
            parameters["client_id"] = state.Options.ClientId;
            parameters["response_type"] = "code";
            parameters["scope"] = string.Join(' ', state.Options.Scopes);
            parameters["redirect_uri"] = state.Options.RedirectUri;
            parameters["state"] = stateValue;
            parameters["nonce"] = nonce;

            if (codeVerifier != null) {
                parameters["code_challenge"] = CreateCodeChallenge(codeVerifier);
                parameters["code_challenge_method"] = "S256";
            }

            return AppendQueryString(configuration.AuthorizationEndpoint, parameters);
        }

        /// <summary>
        /// Complete the OpenID callback by exchanging the authorization code,
        /// validating the ID token, creating a principal, and issuing the auth cookie.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="provider"></param>
        /// <returns></returns>
        public async Task<OpenIDCallbackResult> CompleteCallbackAsync(HttpSession session, string provider) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(session);

            ProviderState state;
            try {
                state = GetProviderState(provider);
            }
            catch (Exception ex) {
                _log.Warn("CompleteCallbackAsync : invalid provider", ex);
                return OpenIDCallbackResult.Fail(provider, 400, "invalid_provider");
            }

            if (session.Request.Query.TryGetValue("error", out string? error)) {
                string description = session.Request.Query.TryGetValue("error_description", out string? errorDescription)
                    ? errorDescription
                    : error;
                return OpenIDCallbackResult.Fail(state.Options.Name, 400, description);
            }

            if (!session.Request.Query.TryGetValue("code", out string? code) || string.IsNullOrWhiteSpace(code)) {
                return OpenIDCallbackResult.Fail(state.Options.Name, 400, "missing_code");
            }

            if (!session.Request.Query.TryGetValue("state", out string? stateValue) || string.IsNullOrWhiteSpace(stateValue)) {
                return OpenIDCallbackResult.Fail(state.Options.Name, 400, "missing_state");
            }

            if (!TryReadChallengeCookie(session, stateValue, out ChallengeTicket? challenge) || challenge is null) {
                return OpenIDCallbackResult.Fail(state.Options.Name, 400, "invalid_state");
            }
            DeleteChallengeCookie(session, stateValue);

            if (!string.Equals(challenge.Provider, state.Options.Name, StringComparison.OrdinalIgnoreCase)) {
                return OpenIDCallbackResult.Fail(state.Options.Name, 400, "provider_state_mismatch", challenge.ReturnUrl);
            }

            if (!string.Equals(challenge.State, stateValue, StringComparison.Ordinal)) {
                return OpenIDCallbackResult.Fail(state.Options.Name, 400, "invalid_state", challenge.ReturnUrl);
            }

            if (challenge.ExpiresAt <= DateTimeOffset.UtcNow) {
                return OpenIDCallbackResult.Fail(state.Options.Name, 400, "expired_state", challenge.ReturnUrl);
            }

            string idToken;
            try {
                (bool isSuccess, string? token, int statusCode, string? tokenError) = await ExchangeCodeAsync(state, code, challenge.CodeVerifier).ConfigureAwait(false);
                if (!isSuccess || string.IsNullOrWhiteSpace(token)) {
                    return OpenIDCallbackResult.Fail(state.Options.Name, statusCode, tokenError, challenge.ReturnUrl);
                }

                idToken = token;
            }
            catch (Exception ex) {
                _log.Warn("CompleteCallbackAsync : unable to exchange authorization code", ex);
                return OpenIDCallbackResult.Fail(state.Options.Name, 502, "token_exchange_failed", challenge.ReturnUrl);
            }

            ClaimsPrincipal claimsPrincipal;
            try {
                claimsPrincipal = await ValidateIdTokenAsync(state, idToken).ConfigureAwait(false);
            }
            catch (Exception ex) {
                _log.Warn("CompleteCallbackAsync : invalid id_token", ex);
                return OpenIDCallbackResult.Fail(state.Options.Name, 401, "invalid_id_token", challenge.ReturnUrl);
            }

            if (state.Options.ValidateNonce) {
                string? nonce = FindFirstValue(claimsPrincipal, "nonce");
                if (!string.Equals(nonce, challenge.Nonce, StringComparison.Ordinal)) {
                    return OpenIDCallbackResult.Fail(state.Options.Name, 401, "invalid_nonce", challenge.ReturnUrl);
                }
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            OpenIDPrincipalContext principalContext = new() {
                Session = session,
                ProviderName = state.Options.Name,
                Provider = state.Options,
                ClaimsPrincipal = claimsPrincipal,
                AuthenticatedAt = now
            };

            HttpPrincipal principal = _options.PrincipalFactory.Invoke(principalContext);
            SetAuthCookie(session, idToken, GetAuthCookieExpiration(claimsPrincipal, now));

            return OpenIDCallbackResult.Success(
                provider: state.Options.Name,
                principal: principal,
                returnUrl: challenge.ReturnUrl
            );
        }

        /// <summary>
        /// Authenticate the current request using the OpenID auth cookie.
        /// The ID token is validated again on every call.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="principal"></param>
        /// <returns></returns>
        public bool TryAuthenticate(HttpSession session, out HttpPrincipal principal) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(session);

            principal = HttpPrincipal.Anonymous;

            try {
                HttpPrincipal? authenticated = AuthenticatePrincipalAsync(session).ConfigureAwait(false).GetAwaiter().GetResult();
                if (authenticated == null) {
                    _log.Trace(() => "TryAuthenticate : missing or invalid OpenID auth cookie");
                    return false;
                }

                principal = authenticated;
                return true;
            }
            catch (Exception ex) {
                _log.Warn("TryAuthenticate", ex);
                return false;
            }
        }

        /// <summary>
        /// Sign out locally by deleting the stateless auth cookie.
        /// Returns the normalized local return URL that the application can redirect to.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="returnUrl"></param>
        /// <returns></returns>
        public string SignOut(HttpSession session, string returnUrl = "/") {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(session);

            DeleteAuthCookie(session);
            session.Principal = HttpPrincipal.Anonymous;

            return NormalizeReturnUrl(returnUrl, _options.AllowExternalReturnUrls);
        }

        /// <summary>
        /// Dispose the owned backchannel HTTP client.
        /// </summary>
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            if (_ownsHttpClient) {
                _httpClient.Dispose();
            }
        }

        #endregion public methods

        #region provider / tokens

        private async Task<HttpPrincipal?> AuthenticatePrincipalAsync(HttpSession session) {
            if (!session.Request.Headers.TryGetCookie(_options.CookieName, out string? ticketValue) || string.IsNullOrWhiteSpace(ticketValue)) {
                return null;
            }

            string idToken = ticketValue;
            (ProviderState? state, ClaimsPrincipal? claimsPrincipal) = await ValidateIdTokenWithAnyProviderAsync(idToken).ConfigureAwait(false);
            if (state == null || claimsPrincipal == null) {
                DeleteAuthCookie(session);
                return null;
            }

            OpenIDPrincipalContext principalContext = new() {
                Session = session,
                ProviderName = state.Options.Name,
                Provider = state.Options,
                ClaimsPrincipal = claimsPrincipal,
                AuthenticatedAt = DateTimeOffset.UtcNow
            };

            return _options.PrincipalFactory.Invoke(principalContext);
        }

        private ProviderState GetProviderState(string provider) {
            if (string.IsNullOrWhiteSpace(provider)) {
                throw new ArgumentException("OpenID provider name must not be null or empty.", nameof(provider));
            }

            if (!_providers.TryGetValue(provider.Trim(), out ProviderState? state)) {
                throw new ArgumentException($"OpenID provider '{provider}' is not configured.", nameof(provider));
            }

            return state;
        }

        private async Task<(bool IsSuccess, string? IdToken, int StatusCode, string? Error)> ExchangeCodeAsync(ProviderState state, string code, string? codeVerifier) {
            OpenIdConnectConfiguration configuration = await state.ConfigurationManager.GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(configuration.TokenEndpoint)) {
                throw new InvalidOperationException($"OpenID provider '{state.Options.Name}' does not expose a token endpoint.");
            }

            List<KeyValuePair<string, string>> form = new() {
                new("grant_type", "authorization_code"),
                new("code", code),
                new("redirect_uri", state.Options.RedirectUri),
                new("client_id", state.Options.ClientId)
            };

            if (!string.IsNullOrWhiteSpace(codeVerifier)) {
                form.Add(new("code_verifier", codeVerifier));
            }

            string? clientSecret = state.Options.ClientSecret;
            if (!string.IsNullOrEmpty(clientSecret) && !state.Options.UseClientSecretBasicAuthentication) {
                form.Add(new("client_secret", clientSecret));
            }

            using HttpRequestMessage request = new(HttpMethod.Post, configuration.TokenEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrEmpty(clientSecret) && state.Options.UseClientSecretBasicAuthentication) {
                string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{state.Options.ClientId}:{clientSecret}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }

            request.Content = new FormUrlEncodedContent(form);

            using HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                string? endpointError = TryReadJsonString(json, "error_description") ?? TryReadJsonString(json, "error");
                int statusCode = (int)response.StatusCode >= 500 ? 502 : 400;
                return (false, null, statusCode, endpointError ?? "token_endpoint_error");
            }

            string? idToken = ParseIdToken(json);
            if (string.IsNullOrWhiteSpace(idToken)) {
                return (false, null, 400, "missing_id_token");
            }

            return (true, idToken, 200, null);
        }

        private async Task<ClaimsPrincipal> ValidateIdTokenAsync(ProviderState state, string idToken) {
            OpenIdConnectConfiguration configuration = await state.ConfigurationManager.GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
            TokenValidationParameters parameters = CreateTokenValidationParameters(state.Options, configuration);

            try {
                ClaimsPrincipal principal = _tokenHandler.ValidateToken(idToken, parameters, out _);
                return principal;
            }
            catch (SecurityTokenSignatureKeyNotFoundException) {
                state.ConfigurationManager.RequestRefresh();
                configuration = await state.ConfigurationManager.GetConfigurationAsync(CancellationToken.None).ConfigureAwait(false);
                parameters = CreateTokenValidationParameters(state.Options, configuration);

                ClaimsPrincipal principal = _tokenHandler.ValidateToken(idToken, parameters, out _);
                return principal;
            }
        }

        private async Task<(ProviderState? State, ClaimsPrincipal? Principal)> ValidateIdTokenWithAnyProviderAsync(string idToken) {
            foreach (ProviderState state in _providers.Values) {
                try {
                    ClaimsPrincipal principal = await ValidateIdTokenAsync(state, idToken).ConfigureAwait(false);
                    return (state, principal);
                }
                catch {
                    // Try the next configured provider.
                }
            }

            return (null, null);
        }

        private static TokenValidationParameters CreateTokenValidationParameters(OpenIDProviderOptions provider, OpenIdConnectConfiguration configuration) {
            TokenValidationParameters parameters = new() {
                ValidateIssuer = provider.ValidateIssuer,
                ValidIssuer = provider.ValidIssuer ?? configuration.Issuer,
                ValidateAudience = true,
                ValidAudience = provider.ClientId,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                ClockSkew = provider.ClockSkew,
                NameClaimType = provider.NameClaimType,
                RoleClaimType = provider.RoleClaimType
            };

            provider.ConfigureTokenValidation?.Invoke(parameters);
            return parameters;
        }

        #endregion provider / tokens

        #region cookies

        private void SetAuthCookie(HttpSession session, string idToken, DateTimeOffset expiresAt) {
            TimeSpan lifetime = expiresAt - DateTimeOffset.UtcNow;

            HttpResponse.CookieOptions options = new(
                path: _options.CookiePath,
                domain: _options.CookieDomain,
                maxAgeSeconds: ToCookieMaxAge(lifetime),
                expires: DateTimeOffset.UtcNow.Add(lifetime),
                secure: _options.CookieSecure,
                httpOnly: _options.CookieHttpOnly,
                sameSite: _options.CookieSameSite
            );

            session.Response.SetCookie(_options.CookieName, idToken, in options);
        }

        private void DeleteAuthCookie(HttpSession session) {
            HttpResponse.CookieOptions options = new(
                path: _options.CookiePath,
                domain: _options.CookieDomain,
                maxAgeSeconds: 0,
                expires: DateTimeOffset.UnixEpoch,
                secure: _options.CookieSecure,
                httpOnly: _options.CookieHttpOnly,
                sameSite: _options.CookieSameSite
            );

            session.Response.SetCookie(_options.CookieName, string.Empty, in options);
        }

        private void SetChallengeCookie(HttpSession session, ChallengeTicket challenge) {
            TimeSpan lifetime = challenge.ExpiresAt - DateTimeOffset.UtcNow;
            string value = Protect(challenge, CookiePurposes.Challenge);
            HttpResponse.CookieOptions options = new(
                path: _options.CookiePath,
                domain: _options.CookieDomain,
                maxAgeSeconds: ToCookieMaxAge(lifetime),
                expires: challenge.ExpiresAt,
                secure: _options.CookieSecure,
                httpOnly: _options.CookieHttpOnly,
                sameSite: _options.CookieSameSite
            );

            session.Response.SetCookie(GetChallengeCookieName(challenge.State), value, in options);
        }

        private bool TryReadChallengeCookie(HttpSession session, string state, out ChallengeTicket? challenge) {
            challenge = null;
            return session.Request.Headers.TryGetCookie(GetChallengeCookieName(state), out string? value)
                   && !string.IsNullOrWhiteSpace(value)
                   && TryUnprotect(value, CookiePurposes.Challenge, out challenge);
        }

        private void DeleteChallengeCookie(HttpSession session, string state) {
            HttpResponse.CookieOptions options = new(
                path: _options.CookiePath,
                domain: _options.CookieDomain,
                maxAgeSeconds: 0,
                expires: DateTimeOffset.UnixEpoch,
                secure: _options.CookieSecure,
                httpOnly: _options.CookieHttpOnly,
                sameSite: _options.CookieSameSite
            );

            session.Response.SetCookie(GetChallengeCookieName(state), string.Empty, in options);
        }

        #endregion cookies

        #region helpers

        private void ThrowIfDisposed() {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        private static int ToCookieMaxAge(TimeSpan lifetime) {
            if (lifetime.TotalSeconds >= int.MaxValue) {
                return int.MaxValue;
            }

            return Math.Max(0, (int)Math.Ceiling(lifetime.TotalSeconds));
        }

        private static string GenerateRandomValue() {
            return Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        }

        private static string CreateCodeChallenge(string codeVerifier) {
            byte[] bytes = Encoding.ASCII.GetBytes(codeVerifier);
            byte[] hash = SHA256.HashData(bytes);
            return Base64UrlEncoder.Encode(hash);
        }

        private string Protect<T>(T value, string purpose) {
            string payload = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(value));
            string signature = Base64UrlEncoder.Encode(ComputeCookieSignature(purpose, payload));
            return payload + "." + signature;
        }

        private bool TryUnprotect<T>(string value, string purpose, out T? result) {
            result = default;

            int separator = value.IndexOf('.');
            if (separator <= 0 || separator >= value.Length - 1) {
                return false;
            }

            string payload = value.Substring(0, separator);
            string signature = value.Substring(separator + 1);
            byte[] expected = ComputeCookieSignature(purpose, payload);
            byte[] actual;

            try {
                actual = Base64UrlEncoder.DecodeBytes(signature);
            }
            catch {
                return false;
            }

            if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected)) {
                return false;
            }

            try {
                byte[] json = Base64UrlEncoder.DecodeBytes(payload);
                result = JsonSerializer.Deserialize<T>(json);
                return result != null;
            }
            catch {
                return false;
            }
        }

        private byte[] ComputeCookieSignature(string purpose, string payload) {
            using HMACSHA256 hmac = new(_challengeProtectionKey);
            byte[] data = Encoding.UTF8.GetBytes(purpose + "." + payload);
            return hmac.ComputeHash(data);
        }

        private DateTimeOffset GetAuthCookieExpiration(ClaimsPrincipal claimsPrincipal, DateTimeOffset now) {
            DateTimeOffset expiresAt = now.Add(_options.SessionLifetime);
            string? exp = FindFirstValue(claimsPrincipal, JwtRegisteredClaimNames.Exp, "exp");

            if (long.TryParse(exp, out long unixSeconds)) {
                DateTimeOffset tokenExpiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                if (tokenExpiresAt < expiresAt) {
                    expiresAt = tokenExpiresAt;
                }
            }

            return expiresAt;
        }

        private static string AppendQueryString(string url, IReadOnlyDictionary<string, string> parameters) {
            StringBuilder builder = new(url);
            bool hasQuery = url.Contains('?', StringComparison.Ordinal);

            if (!url.EndsWith("?", StringComparison.Ordinal) && !url.EndsWith("&", StringComparison.Ordinal)) {
                builder.Append(hasQuery ? '&' : '?');
            }

            bool first = true;
            foreach (KeyValuePair<string, string> parameter in parameters) {
                if (!first) {
                    builder.Append('&');
                }

                builder.Append(Uri.EscapeDataString(parameter.Key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(parameter.Value ?? string.Empty));
                first = false;
            }

            return builder.ToString();
        }

        private static string? ParseIdToken(string json) {
            using JsonDocument document = JsonDocument.Parse(json);
            return GetString(document.RootElement, "id_token");
        }

        private static string? TryReadJsonString(string json, string property) {
            if (string.IsNullOrWhiteSpace(json)) {
                return null;
            }

            try {
                using JsonDocument document = JsonDocument.Parse(json);
                return GetString(document.RootElement, property);
            }
            catch {
                return null;
            }
        }

        private static string? GetString(JsonElement root, string property) {
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(property, out JsonElement value)
                && value.ValueKind == JsonValueKind.String) {
                return value.GetString();
            }

            return null;
        }

        private static string NormalizeReturnUrl(string? returnUrl, bool allowExternal) {
            if (string.IsNullOrWhiteSpace(returnUrl)) {
                return "/";
            }

            string trimmed = returnUrl.Trim();
            if (allowExternal) {
                return trimmed;
            }

            if (trimmed.StartsWith("/", StringComparison.Ordinal) && !trimmed.StartsWith("//", StringComparison.Ordinal)) {
                return trimmed;
            }

            return "/";
        }

        private string GetChallengeCookieName(string state) {
            return _options.ChallengeCookieNamePrefix + state;
        }

        private static string? FindFirstValue(ClaimsPrincipal principal, params string[] claimTypes) {
            foreach (string claimType in claimTypes) {
                string? value = principal.FindFirst(claimType)?.Value;
                if (!string.IsNullOrWhiteSpace(value)) {
                    return value;
                }
            }

            return null;
        }

        #endregion helpers

        #region internal types

        private static class CookiePurposes {
            public const string Challenge = "OpenID.Challenge";
        }

        private sealed record ChallengeTicket(
            string Provider,
            string State,
            string Nonce,
            string? CodeVerifier,
            string ReturnUrl,
            DateTimeOffset ExpiresAt
        );

        private sealed record ProviderState(
            OpenIDProviderOptions Options,
            ConfigurationManager<OpenIdConnectConfiguration> ConfigurationManager
        );

        #endregion internal types

    }

}
