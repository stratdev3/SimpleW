using System.Globalization;
using System.Security.Claims;
using System.Text.Json;


namespace SimpleW.Helper.OpenID {

    /// <summary>
    /// Options for OpenIDHelper.
    /// </summary>
    public class OpenIDHelperOptions {

        #region properties

        private readonly Dictionary<string, OpenIDProviderOptions> _providers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Configured OpenID providers.
        /// </summary>
        public IReadOnlyDictionary<string, OpenIDProviderOptions> Providers => _providers;

        /// <summary>
        /// Auth cookie name.
        /// </summary>
        public string CookieName { get; set; } = "sw_oidc";

        /// <summary>
        /// Prefix used for temporary OpenID challenge cookies.
        /// </summary>
        public string ChallengeCookieNamePrefix { get; set; } = "sw_oidc_challenge_";

        /// <summary>
        /// Auth cookie path.
        /// </summary>
        public string CookiePath { get; set; } = "/";

        /// <summary>
        /// Auth cookie domain.
        /// </summary>
        public string? CookieDomain { get; set; }

        /// <summary>
        /// Auth cookie Secure flag.
        /// </summary>
        public bool CookieSecure { get; set; } = true;

        /// <summary>
        /// Auth cookie HttpOnly flag.
        /// </summary>
        public bool CookieHttpOnly { get; set; } = true;

        /// <summary>
        /// Auth cookie SameSite mode.
        /// </summary>
        public HttpResponse.SameSiteMode CookieSameSite { get; set; } = HttpResponse.SameSiteMode.Lax;

        /// <summary>
        /// Maximum auth cookie lifetime. The cookie also expires no later than the provider ID token.
        /// </summary>
        public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);

        /// <summary>
        /// Authorization challenge lifetime.
        /// </summary>
        public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Backchannel HTTP timeout when the helper creates its own HttpClient.
        /// </summary>
        public TimeSpan BackchannelTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Optional backchannel HTTP client.
        /// </summary>
        public HttpClient? Backchannel { get; set; }

        /// <summary>
        /// Allow absolute/external returnUrl values.
        /// Disabled by default to avoid open redirects.
        /// </summary>
        public bool AllowExternalReturnUrls { get; set; }

        /// <summary>
        /// Key used to sign temporary OpenID challenge cookies.
        /// Auth cookies store the provider-issued ID token and do not depend on this key.
        /// If null, a process-local random key is generated at helper startup.
        /// </summary>
        public byte[]? CookieProtectionKey { get; set; }

        /// <summary>
        /// Principal factory used to map validated OpenID claims to a HttpPrincipal.
        /// </summary>
        public Func<OpenIDPrincipalContext, HttpPrincipal> PrincipalFactory { get; set; } = CreateDefaultPrincipal;

        #endregion properties

        #region providers

        /// <summary>
        /// Add or replace an OpenID provider.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public OpenIDHelperOptions Add(string name, Action<OpenIDProviderOptions> configure) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Provider name must not be null or empty.", nameof(name));
            }

            ArgumentNullException.ThrowIfNull(configure);

            OpenIDProviderOptions provider = new() {
                Name = name.Trim()
            };
            configure(provider);
            provider.Name = name.Trim();

            _providers[provider.Name] = provider;
            return this;
        }

        #endregion providers

        #region validation

        /// <summary>
        /// Validate and normalize options.
        /// </summary>
        /// <returns></returns>
        public OpenIDHelperOptions ValidateAndNormalize() {
            if (_providers.Count == 0) {
                throw new ArgumentException("At least one OpenID provider must be configured.", nameof(Providers));
            }

            if (string.IsNullOrWhiteSpace(CookieName)) {
                throw new ArgumentException($"{nameof(CookieName)} must not be null or empty.", nameof(CookieName));
            }

            CookieName = CookieName.Trim();
            ChallengeCookieNamePrefix = string.IsNullOrWhiteSpace(ChallengeCookieNamePrefix) ? "sw_oidc_challenge_" : ChallengeCookieNamePrefix.Trim();
            CookiePath = string.IsNullOrWhiteSpace(CookiePath) ? "/" : CookiePath.Trim();

            if (SessionLifetime <= TimeSpan.Zero) {
                throw new ArgumentException($"{nameof(SessionLifetime)} must be greater than zero.", nameof(SessionLifetime));
            }

            if (ChallengeLifetime <= TimeSpan.Zero) {
                throw new ArgumentException($"{nameof(ChallengeLifetime)} must be greater than zero.", nameof(ChallengeLifetime));
            }

            if (BackchannelTimeout <= TimeSpan.Zero) {
                throw new ArgumentException($"{nameof(BackchannelTimeout)} must be greater than zero.", nameof(BackchannelTimeout));
            }

            if (CookieProtectionKey != null && CookieProtectionKey.Length < 32) {
                throw new ArgumentException($"{nameof(CookieProtectionKey)} must be at least 32 bytes.", nameof(CookieProtectionKey));
            }

            foreach (OpenIDProviderOptions provider in _providers.Values) {
                provider.ValidateAndNormalize();
            }

            return this;
        }

        #endregion validation

        #region principal factory

        private static HttpPrincipal CreateDefaultPrincipal(OpenIDPrincipalContext context) {
            ClaimsPrincipal claims = context.ClaimsPrincipal;

            string? subject = FindFirstValue(claims, "sub", ClaimTypes.NameIdentifier);
            string? name = FindFirstValue(claims, "name", "preferred_username", "unique_name", ClaimTypes.Name);
            string? email = FindFirstValue(claims, "email", ClaimTypes.Email);
            string[] roles = GetRoles(claims, context.Provider.RoleClaimTypes);

            List<IdentityProperty> properties = new() {
                new("provider", context.ProviderName),
                new("auth_scheme", "OpenID"),
                new("auth_time", context.AuthenticatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            };

            if (!string.IsNullOrWhiteSpace(subject)) {
                properties.Add(new IdentityProperty("subject", subject));
            }

            string? login = name ?? email ?? subject;
            if (!string.IsNullOrWhiteSpace(login)) {
                properties.Add(new IdentityProperty("login", login));
            }

            string? issuer = FindFirstValue(claims, "iss");
            if (!string.IsNullOrWhiteSpace(issuer)) {
                properties.Add(new IdentityProperty("issuer", issuer));
            }

            foreach (Claim claim in claims.Claims) {
                if (!string.IsNullOrWhiteSpace(claim.Type) && claim.Value != null) {
                    properties.Add(new IdentityProperty(claim.Type, claim.Value));
                }
            }

            return new HttpPrincipal(new HttpIdentity(
                isAuthenticated: true,
                authenticationType: $"OpenID:{context.ProviderName}",
                identifier: subject,
                name: name,
                email: email,
                roles: roles,
                properties: properties
            ));
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

        private static string[] GetRoles(ClaimsPrincipal principal, string[] roleClaimTypes) {
            HashSet<string> roles = new(StringComparer.OrdinalIgnoreCase);
            foreach (Claim claim in principal.Claims) {
                if (!roleClaimTypes.Contains(claim.Type, StringComparer.Ordinal)) {
                    continue;
                }

                foreach (string role in ExpandClaimValue(claim.Value)) {
                    if (!string.IsNullOrWhiteSpace(role)) {
                        roles.Add(role.Trim());
                    }
                }
            }

            return roles.ToArray();
        }

        private static IEnumerable<string> ExpandClaimValue(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                yield break;
            }

            string trimmed = value.Trim();
            if (trimmed.Length > 1 && trimmed[0] == '[') {
                string[]? values = TryParseStringArray(trimmed);
                if (values != null) {
                    foreach (string item in values) {
                        yield return item;
                    }
                    yield break;
                }
            }

            yield return value;
        }

        private static string[]? TryParseStringArray(string value) {
            try {
                using JsonDocument document = JsonDocument.Parse(value);
                if (document.RootElement.ValueKind != JsonValueKind.Array) {
                    return null;
                }

                List<string> values = new();
                foreach (JsonElement element in document.RootElement.EnumerateArray()) {
                    if (element.ValueKind == JsonValueKind.String) {
                        string? item = element.GetString();
                        if (!string.IsNullOrWhiteSpace(item)) {
                            values.Add(item);
                        }
                    }
                }

                return values.ToArray();
            }
            catch {
                return null;
            }
        }

        #endregion principal factory

    }

}
