using System.Globalization;
using System.Text;


namespace SimpleW.Helper.Jwt {

    /// <summary>
    /// Options for JwtBearerHelper.
    /// </summary>
    public class JwtBearerOptions {

        /// <summary>
        /// Precomputed shared secret key bytes used to validate and create HMAC JWT tokens.
        /// </summary>
        internal byte[] SecretKeyBytes { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// Shared secret key used to sign and validate HMAC JWT tokens.
        /// </summary>
        public string SecretKey { get; set; } = string.Empty;

        /// <summary>
        /// Issuer written to the token and validated on read when set.
        /// </summary>
        public string? Issuer {
            get => ExpectedIssuer;
            set => ExpectedIssuer = value;
        }

        /// <summary>
        /// Audience written to the token and validated on read when set.
        /// </summary>
        public string? Audience {
            get => ExpectedAudience;
            set => ExpectedAudience = value;
        }

        /// <summary>
        /// Expected issuer (iss). Null = do not validate issuer.
        /// </summary>
        public string? ExpectedIssuer { get; set; }

        /// <summary>
        /// Expected audience (aud). Null = do not validate audience.
        /// </summary>
        public string? ExpectedAudience { get; set; }

        /// <summary>
        /// Allowed clock skew for exp / nbf validation (default 1 minute).
        /// </summary>
        public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// JWT HMAC algorithm: HS256 / HS384 / HS512 (default HS256).
        /// </summary>
        public string Algorithm { get; set; } = "HS256";

        /// <summary>
        /// Authorization scheme read from the Authorization header.
        /// </summary>
        public string Scheme { get; set; } = "Bearer";

        /// <summary>
        /// Authentication type used for the rebuilt HttpIdentity.
        /// </summary>
        public string AuthenticationType { get; set; } = "Bearer";

        /// <summary>
        /// Principal factory used to map a validated JWT token to a HttpPrincipal.
        /// </summary>
        public Func<JwtPrincipalContext, HttpPrincipal> PrincipalFactory { get; set; } = CreateDefaultPrincipal;

        /// <summary>
        /// Convenience builder.
        /// </summary>
        /// <param name="secretKey"></param>
        /// <param name="issuer"></param>
        /// <param name="audience"></param>
        /// <param name="clockSkew"></param>
        /// <param name="algorithm"></param>
        /// <returns></returns>
        public static JwtBearerOptions Create(
            string secretKey,
            string? issuer = null,
            string? audience = null,
            TimeSpan? clockSkew = null,
            string algorithm = "HS256"
        ) {
            JwtBearerOptions options = new() {
                SecretKey = secretKey,
                Issuer = issuer,
                Audience = audience,
                ClockSkew = clockSkew ?? TimeSpan.FromMinutes(1),
                Algorithm = algorithm
            };

            return options.ValidateAndNormalize();
        }

        /// <summary>
        /// Validate and normalize options.
        /// </summary>
        /// <returns></returns>
        public JwtBearerOptions ValidateAndNormalize() {
            if (string.IsNullOrWhiteSpace(SecretKey)) {
                throw new ArgumentException($"{nameof(SecretKey)} must not be null or empty.", nameof(SecretKey));
            }

            if (ClockSkew < TimeSpan.Zero) {
                throw new ArgumentException($"{nameof(ClockSkew)} must be greater than or equal to zero.", nameof(ClockSkew));
            }

            Algorithm = NormalizeAlgorithm(Algorithm);
            Scheme = string.IsNullOrWhiteSpace(Scheme) ? "Bearer" : Scheme.Trim();
            AuthenticationType = string.IsNullOrWhiteSpace(AuthenticationType) ? Scheme : AuthenticationType.Trim();

            ExpectedIssuer = string.IsNullOrWhiteSpace(ExpectedIssuer) ? null : ExpectedIssuer.Trim();
            ExpectedAudience = string.IsNullOrWhiteSpace(ExpectedAudience) ? null : ExpectedAudience.Trim();
            SecretKeyBytes = Encoding.UTF8.GetBytes(SecretKey);
            return this;
        }

        private static string NormalizeAlgorithm(string? algorithm) {
            string normalized = (algorithm ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized is not ("HS256" or "HS384" or "HS512")) {
                throw new ArgumentException("Invalid algorithm", nameof(algorithm));
            }

            return normalized;
        }

        private static HttpPrincipal CreateDefaultPrincipal(JwtPrincipalContext context) {
            List<IdentityProperty> properties = new() {
                new("auth_scheme", context.Scheme),
                new("auth_time", context.AuthenticatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
            };

            if (!string.IsNullOrWhiteSpace(context.Subject)) {
                properties.Add(new IdentityProperty("subject", context.Subject));
            }

            string? login = context.Name ?? context.Email ?? context.Subject;
            if (!string.IsNullOrWhiteSpace(login)) {
                properties.Add(new IdentityProperty("login", login));
            }

            if (!string.IsNullOrWhiteSpace(context.Issuer)) {
                properties.Add(new IdentityProperty("issuer", context.Issuer));
            }

            foreach (string audience in context.Audiences) {
                if (!string.IsNullOrWhiteSpace(audience)) {
                    properties.Add(new IdentityProperty("audience", audience));
                }
            }

            properties.AddRange(context.Properties);

            return new HttpPrincipal(new HttpIdentity(
                isAuthenticated: true,
                authenticationType: string.IsNullOrWhiteSpace(context.AuthenticationType) ? context.Scheme : context.AuthenticationType,
                identifier: context.Subject,
                name: context.Name,
                email: context.Email,
                roles: context.Roles,
                properties: properties
            ));
        }

    }

}
