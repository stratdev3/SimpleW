using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;


namespace SimpleW.Helper.OpenID {

    /// <summary>
    /// Per-provider OpenID Connect configuration.
    /// </summary>
    public class OpenIDProviderOptions {

        #region properties

        /// <summary>
        /// Provider logical name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Provider authority, for example https://accounts.google.com.
        /// </summary>
        public string Authority { get; set; } = string.Empty;

        /// <summary>
        /// Explicit metadata address. Defaults to Authority + "/.well-known/openid-configuration".
        /// </summary>
        public string? MetadataAddress { get; set; }

        /// <summary>
        /// OAuth/OpenID client id.
        /// </summary>
        public string ClientId { get; set; } = string.Empty;

        /// <summary>
        /// OAuth/OpenID client secret. Optional for public clients.
        /// </summary>
        public string? ClientSecret { get; set; }

        /// <summary>
        /// Redirect URI registered at the provider.
        /// </summary>
        public string RedirectUri { get; set; } = string.Empty;

        /// <summary>
        /// Requested scopes.
        /// </summary>
        public string[] Scopes { get; set; } = [ "openid", "profile", "email" ];

        /// <summary>
        /// Extra authorization endpoint parameters such as prompt or login_hint.
        /// </summary>
        public Dictionary<string, string> AuthorizationParameters { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Use PKCE for authorization code flow.
        /// </summary>
        public bool UsePkce { get; set; } = true;

        /// <summary>
        /// Validate nonce returned in the id_token.
        /// </summary>
        public bool ValidateNonce { get; set; } = true;

        /// <summary>
        /// Require HTTPS metadata endpoint.
        /// </summary>
        public bool RequireHttpsMetadata { get; set; } = true;

        /// <summary>
        /// Validate token issuer.
        /// </summary>
        public bool ValidateIssuer { get; set; } = true;

        /// <summary>
        /// Override the valid issuer. Defaults to the metadata issuer.
        /// </summary>
        public string? ValidIssuer { get; set; }

        /// <summary>
        /// Send client_id/client_secret using the Authorization Basic header instead of the token endpoint form body.
        /// </summary>
        public bool UseClientSecretBasicAuthentication { get; set; }

        /// <summary>
        /// Token validation clock skew.
        /// </summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Claim used by ClaimsPrincipal for name.
        /// </summary>
        public string NameClaimType { get; set; } = "name";

        /// <summary>
        /// Claim used by ClaimsPrincipal for roles.
        /// </summary>
        public string RoleClaimType { get; set; } = "role";

        /// <summary>
        /// Role claim types used by the default HttpPrincipal mapper.
        /// </summary>
        public string[] RoleClaimTypes { get; set; } = [ "role", "roles", ClaimTypes.Role ];

        /// <summary>
        /// Advanced hook to adjust token validation parameters.
        /// </summary>
        public Action<TokenValidationParameters>? ConfigureTokenValidation { get; set; }

        #endregion properties

        #region validation

        /// <summary>
        /// Validate and normalize provider options.
        /// </summary>
        /// <returns></returns>
        public OpenIDProviderOptions ValidateAndNormalize() {
            if (string.IsNullOrWhiteSpace(Name)) {
                throw new ArgumentException($"{nameof(OpenIDProviderOptions)}.{nameof(Name)} must not be null or empty.", nameof(Name));
            }

            Name = Name.Trim();
            Authority = Authority?.Trim().TrimEnd('/') ?? string.Empty;
            MetadataAddress = string.IsNullOrWhiteSpace(MetadataAddress)
                                ? BuildMetadataAddress(Authority)
                                : MetadataAddress.Trim();

            if (string.IsNullOrWhiteSpace(MetadataAddress)) {
                throw new ArgumentException($"{nameof(OpenIDProviderOptions)}.{nameof(Authority)} or {nameof(MetadataAddress)} must be configured for provider '{Name}'.");
            }

            if (string.IsNullOrWhiteSpace(ClientId)) {
                throw new ArgumentException($"{nameof(OpenIDProviderOptions)}.{nameof(ClientId)} must be configured for provider '{Name}'.");
            }

            if (string.IsNullOrWhiteSpace(RedirectUri)) {
                throw new ArgumentException($"{nameof(OpenIDProviderOptions)}.{nameof(RedirectUri)} must be configured for provider '{Name}'.");
            }

            ClientId = ClientId.Trim();
            ClientSecret = string.IsNullOrEmpty(ClientSecret) ? null : ClientSecret;
            RedirectUri = RedirectUri.Trim();

            Scopes = NormalizeScopes(Scopes);
            AuthorizationParameters ??= new Dictionary<string, string>(StringComparer.Ordinal);
            RoleClaimTypes = (RoleClaimTypes ?? Array.Empty<string>())
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (RoleClaimTypes.Length == 0) {
                RoleClaimTypes = [ "role", "roles", ClaimTypes.Role ];
            }

            if (ClockSkew < TimeSpan.Zero) {
                throw new ArgumentException($"{nameof(ClockSkew)} must not be negative.", nameof(ClockSkew));
            }

            NameClaimType = string.IsNullOrWhiteSpace(NameClaimType) ? "name" : NameClaimType.Trim();
            RoleClaimType = string.IsNullOrWhiteSpace(RoleClaimType) ? "role" : RoleClaimType.Trim();
            ValidIssuer = string.IsNullOrWhiteSpace(ValidIssuer) ? null : ValidIssuer.Trim();

            return this;
        }

        private static string? BuildMetadataAddress(string authority) {
            if (string.IsNullOrWhiteSpace(authority)) {
                return null;
            }

            return authority.Trim().TrimEnd('/') + "/.well-known/openid-configuration";
        }

        private static string[] NormalizeScopes(string[]? scopes) {
            string[] normalized = (scopes ?? Array.Empty<string>())
                .Where(static s => !string.IsNullOrWhiteSpace(s))
                .Select(static s => s.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (normalized.Length == 0) {
                normalized = [ "openid", "profile", "email" ];
            }

            if (!normalized.Contains("openid", StringComparer.Ordinal)) {
                normalized = [ "openid", .. normalized ];
            }

            return normalized;
        }

        #endregion validation

    }

}
