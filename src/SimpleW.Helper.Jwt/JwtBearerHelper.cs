using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleW.Observability;


namespace SimpleW.Helper.Jwt {

    /// <summary>
    /// Stateless helper for HTTP Bearer JWT authentication.
    /// It parses the Authorization header, validates the token,
    /// and can build a SimpleW principal when authentication succeeds.
    /// Policy decisions such as route protection remain in user middleware.
    /// </summary>
    public sealed class JwtBearerHelper {

        /// <summary>
        /// Options
        /// </summary>
        private readonly JwtBearerOptions _options;

        /// <summary>
        /// Principal Factory
        /// </summary>
        private readonly Func<JwtPrincipalContext, HttpPrincipal> _principalFactory;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="configure"></param>
        public JwtBearerHelper(Action<JwtBearerOptions> configure) {
            ArgumentNullException.ThrowIfNull(configure);

            JwtBearerOptions options = new();
            configure(options);
            options.ValidateAndNormalize();

            _options = options;
            _principalFactory = options.PrincipalFactory;
        }

        /// <summary>
        /// Constructor from an existing options instance.
        /// A normalized clone is created so caller-owned options stay reusable.
        /// </summary>
        /// <param name="options"></param>
        public JwtBearerHelper(JwtBearerOptions options) {
            ArgumentNullException.ThrowIfNull(options);

            JwtBearerOptions cloned = CloneOptions(options);
            cloned.ValidateAndNormalize();

            _options = cloned;
            _principalFactory = cloned.PrincipalFactory;
        }

        /// <summary>
        /// Create JWT from a principal.
        /// </summary>
        /// <param name="principal"></param>
        /// <param name="lifetime"></param>
        /// <param name="issuer"></param>
        /// <param name="audience"></param>
        /// <param name="nowUtc"></param>
        /// <returns></returns>
        public string CreateToken(HttpPrincipal principal, TimeSpan lifetime, string? issuer = null, string? audience = null, DateTimeOffset? nowUtc = null) {
            ArgumentNullException.ThrowIfNull(principal);
            return CreateToken(principal.Identity, lifetime, issuer, audience, nowUtc);
        }

        /// <summary>
        /// Create JWT from an identity.
        /// </summary>
        /// <param name="identity"></param>
        /// <param name="lifetime"></param>
        /// <param name="issuer"></param>
        /// <param name="audience"></param>
        /// <param name="nowUtc"></param>
        /// <returns></returns>
        public string CreateToken(HttpIdentity identity, TimeSpan lifetime, string? issuer = null, string? audience = null, DateTimeOffset? nowUtc = null) {
            ArgumentNullException.ThrowIfNull(identity);

            if (lifetime < TimeSpan.Zero) {
                throw new ArgumentException($"{nameof(lifetime)} must be greater than or equal to zero.", nameof(lifetime));
            }

            DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;
            DateTimeOffset exp = now.Add(lifetime);

            Dictionary<string, object?> payload = new(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(identity.Identifier)) {
                payload["sub"] = identity.Identifier;
            }

            if (!string.IsNullOrWhiteSpace(identity.Name)) {
                payload["name"] = identity.Name;
            }

            if (!string.IsNullOrWhiteSpace(identity.Email)) {
                payload["email"] = identity.Email;
            }

            issuer ??= _options.ExpectedIssuer;
            audience ??= _options.ExpectedAudience;

            if (!string.IsNullOrWhiteSpace(issuer)) {
                payload["iss"] = issuer;
            }

            if (!string.IsNullOrWhiteSpace(audience)) {
                payload["aud"] = audience;
            }

            payload["iat"] = now.ToUnixTimeSeconds();
            payload["nbf"] = now.ToUnixTimeSeconds();
            payload["exp"] = exp.ToUnixTimeSeconds();

            if (identity.Roles.Count == 1) {
                foreach (string role in identity.Roles) {
                    payload["role"] = role;
                    break;
                }
            }
            else if (identity.Roles.Count > 1) {
                payload["roles"] = identity.Roles.ToArray();
            }

            foreach (IdentityProperty property in identity.Properties) {
                if (string.IsNullOrWhiteSpace(property.Key)) {
                    continue;
                }

                // Keep explicit core claim mapping priority.
                if (payload.ContainsKey(property.Key)) {
                    continue;
                }

                payload[property.Key] = property.Value;
            }

            Dictionary<string, object?> header = new(StringComparer.Ordinal) {
                ["alg"] = _options.Algorithm,
                ["typ"] = "JWT"
            };

            string encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
            string encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
            string signingInput = encodedHeader + "." + encodedPayload;

            byte[] signature = ComputeSignature(_options.Algorithm, Encoding.ASCII.GetBytes(signingInput), _options.SecretKeyBytes);
            return signingInput + "." + Base64UrlEncode(signature);
        }

        /// <summary>
        /// Validate JWT token and rebuild a principal.
        /// </summary>
        /// <param name="token"></param>
        /// <param name="principal"></param>
        /// <param name="error"></param>
        /// <param name="nowUtc"></param>
        /// <returns></returns>
        public bool TryValidateToken(string token, out HttpPrincipal? principal, out string? error, DateTimeOffset? nowUtc = null) {
            return TryValidateTokenCore(token, session: null, out principal, out error, nowUtc);
        }

        /// <summary>
        /// Authenticate the current request using the Authorization header.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="principal"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool TryAuthenticate(HttpSession session, out HttpPrincipal principal, out string? error) {
            ArgumentNullException.ThrowIfNull(session);
            principal = HttpPrincipal.Anonymous;
            error = null;

            try {
                string? authorization = session.Request.Headers.Authorization;
                if (string.IsNullOrWhiteSpace(authorization)) {
                    error = "missing header authorization";
                    return false;
                }

                if (!TryParseAuthorizationHeader(authorization, _options.Scheme, out string token)) {
                    error = "unable to parse header authorization";
                    return false;
                }

                if (!TryValidateTokenCore(token, session, out HttpPrincipal? authenticated, out string? error1)) {
                    error = $"invalid jwt token ({error1 ?? "unknown_error"})";
                    return false;
                }

                principal = authenticated ?? HttpPrincipal.Anonymous;
                return true;
            }
            catch (Exception ex) {
                error = $"{ex.Message} {ex.Source} {ex.InnerException?.Message} {ex.InnerException?.Source}";
                return false;
            }
        }

        private bool TryValidateTokenCore(string token, HttpSession? session, out HttpPrincipal? principal, out string? error, DateTimeOffset? nowUtc = null) {
            principal = null;
            error = null;

            if (string.IsNullOrWhiteSpace(token)) {
                error = "JWT token is empty.";
                return false;
            }

            string[] parts = token.Split('.');
            if (parts.Length != 3) {
                error = "JWT must contain exactly 3 parts.";
                return false;
            }

            string encodedHeader = parts[0];
            string encodedPayload = parts[1];
            string encodedSignature = parts[2];

            byte[] headerBytes;
            byte[] payloadBytes;
            byte[] signatureBytes;

            try {
                headerBytes = Base64UrlDecode(encodedHeader);
                payloadBytes = Base64UrlDecode(encodedPayload);
                signatureBytes = Base64UrlDecode(encodedSignature);
            }
            catch {
                error = "JWT contains invalid Base64Url data.";
                return false;
            }

            JwtHeader? header;
            JwtPayload? payload;

            try {
                header = JsonSerializer.Deserialize<JwtHeader>(headerBytes);
                payload = JsonSerializer.Deserialize<JwtPayload>(payloadBytes);
            }
            catch {
                error = "JWT header or payload is not valid JSON.";
                return false;
            }

            if (header == null) {
                error = "JWT header is missing.";
                return false;
            }

            if (payload == null) {
                error = "JWT payload is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(header.Alg)) {
                error = "JWT header 'alg' is missing.";
                return false;
            }

            if (!string.Equals(header.Alg, _options.Algorithm, StringComparison.Ordinal)) {
                error = "JWT algorithm is invalid.";
                return false;
            }

            if (!TryVerifySignature(header.Alg, encodedHeader, encodedPayload, signatureBytes, _options.SecretKeyBytes)) {
                error = "JWT signature is invalid.";
                return false;
            }

            DateTimeOffset now = nowUtc ?? DateTimeOffset.UtcNow;

            if (payload.Exp.HasValue) {
                DateTimeOffset exp = DateTimeOffset.FromUnixTimeSeconds(payload.Exp.Value);
                if (now > exp + _options.ClockSkew) {
                    error = "JWT token is expired.";
                    return false;
                }
            }

            if (payload.Nbf.HasValue) {
                DateTimeOffset nbf = DateTimeOffset.FromUnixTimeSeconds(payload.Nbf.Value);
                if (now + _options.ClockSkew < nbf) {
                    error = "JWT token is not active yet.";
                    return false;
                }
            }

            if (_options.ExpectedIssuer != null
                && !string.Equals(payload.Iss, _options.ExpectedIssuer, StringComparison.Ordinal)
            ) {
                error = "JWT issuer is invalid.";
                return false;
            }

            string[] audiences = GetAudiences(payload.Aud);
            if (_options.ExpectedAudience != null
                && !audiences.Contains(_options.ExpectedAudience, StringComparer.Ordinal)
            ) {
                error = "JWT audience is invalid.";
                return false;
            }

            JwtPrincipalContext context = new() {
                Session = session,
                Token = token,
                Subject = payload.Sub,
                Name = payload.Name,
                Email = payload.Email,
                Issuer = payload.Iss,
                Audiences = audiences,
                Roles = GetRoles(payload),
                Properties = BuildProperties(payload),
                AuthenticatedAt = now,
                AuthenticationType = _options.AuthenticationType,
                Scheme = _options.Scheme
            };

            principal = _principalFactory.Invoke(context);
            if (principal == null) {
                error = "JWT principal factory returned null.";
                return false;
            }

            return true;
        }

        private static JwtBearerOptions CloneOptions(JwtBearerOptions options) {
            return new JwtBearerOptions {
                SecretKey = options.SecretKey,
                ExpectedIssuer = options.ExpectedIssuer,
                ExpectedAudience = options.ExpectedAudience,
                ClockSkew = options.ClockSkew,
                Algorithm = options.Algorithm,
                Scheme = options.Scheme,
                AuthenticationType = options.AuthenticationType,
                PrincipalFactory = options.PrincipalFactory
            };
        }

        #region helpers

        private static bool TryParseAuthorizationHeader(string authorization, string scheme, out string token) {
            token = string.Empty;

            ReadOnlySpan<char> span = authorization.AsSpan().Trim();
            ReadOnlySpan<char> schemeSpan = scheme.AsSpan();

            if (!span.StartsWith(schemeSpan, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            if (span.Length <= schemeSpan.Length || !char.IsWhiteSpace(span[schemeSpan.Length])) {
                return false;
            }

            span = span.Slice(schemeSpan.Length).TrimStart();
            if (span.Length == 0) {
                return false;
            }

            token = span.ToString();
            return true;
        }

        private static bool TryVerifySignature(string algorithm, string encodedHeader, string encodedPayload, byte[] providedSignature, byte[] key) {
            byte[] signingInputBytes = Encoding.ASCII.GetBytes(encodedHeader + "." + encodedPayload);
            byte[] computedSignature;

            try {
                computedSignature = ComputeSignature(algorithm, signingInputBytes, key);
            }
            catch {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(computedSignature, providedSignature);
        }

        private static byte[] ComputeSignature(string algorithm, byte[] data, byte[] key) {
            return algorithm switch {
                "HS256" => ComputeHmacSha256(data, key),
                "HS384" => ComputeHmacSha384(data, key),
                "HS512" => ComputeHmacSha512(data, key),
                _ => throw new NotSupportedException("JWT algorithm '" + algorithm + "' is not supported.")
            };
        }

        private static byte[] ComputeHmacSha256(byte[] data, byte[] key) {
            using HMACSHA256 hmac = new(key);
            return hmac.ComputeHash(data);
        }

        private static byte[] ComputeHmacSha384(byte[] data, byte[] key) {
            using HMACSHA384 hmac = new(key);
            return hmac.ComputeHash(data);
        }

        private static byte[] ComputeHmacSha512(byte[] data, byte[] key) {
            using HMACSHA512 hmac = new(key);
            return hmac.ComputeHash(data);
        }

        private static string[] GetRoles(JwtPayload payload) {
            HashSet<string> roles = new(StringComparer.Ordinal);

            foreach (KeyValuePair<string, JsonElement> kv in payload.Extra) {
                if (!string.Equals(kv.Key, "role", StringComparison.Ordinal)
                    && !string.Equals(kv.Key, "roles", StringComparison.Ordinal)) {
                    continue;
                }

                AddRoles(roles, kv.Value);
            }

            return roles.ToArray();
        }

        private static IReadOnlyList<IdentityProperty> BuildProperties(JwtPayload payload) {
            List<IdentityProperty> properties = new();

            foreach (KeyValuePair<string, JsonElement> kv in payload.Extra) {
                string key = kv.Key;
                JsonElement value = kv.Value;

                switch (key) {
                    case "sub":
                    case "name":
                    case "email":
                    case "iss":
                    case "aud":
                    case "exp":
                    case "nbf":
                    case "iat":
                    case "role":
                    case "roles":
                        break;

                    default:
                        AddProperty(properties, key, value);
                        break;
                }
            }

            return properties;
        }

        private static void AddRoles(HashSet<string> roles, JsonElement value) {
            if (value.ValueKind == JsonValueKind.String) {
                string? role = value.GetString();
                if (!string.IsNullOrWhiteSpace(role)) {
                    roles.Add(role);
                }
                return;
            }

            if (value.ValueKind == JsonValueKind.Array) {
                foreach (JsonElement item in value.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.String) {
                        continue;
                    }

                    string? role = item.GetString();
                    if (!string.IsNullOrWhiteSpace(role)) {
                        roles.Add(role);
                    }
                }
            }
        }

        private static string[] GetAudiences(JsonElement value) {
            if (value.ValueKind == JsonValueKind.String) {
                string? audience = value.GetString();
                if (!string.IsNullOrWhiteSpace(audience)) {
                    return [ audience ];
                }
            }

            if (value.ValueKind == JsonValueKind.Array) {
                List<string> audiences = new();
                foreach (JsonElement item in value.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.String) {
                        continue;
                    }

                    string? audience = item.GetString();
                    if (!string.IsNullOrWhiteSpace(audience)) {
                        audiences.Add(audience);
                    }
                }

                return audiences.Distinct(StringComparer.Ordinal).ToArray();
            }

            return Array.Empty<string>();
        }

        private static void AddProperty(List<IdentityProperty> properties, string key, JsonElement value) {
            switch (value.ValueKind) {
                case JsonValueKind.String:
                    properties.Add(new IdentityProperty(key, value.GetString() ?? string.Empty));
                    return;

                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    properties.Add(new IdentityProperty(key, value.ToString()));
                    return;

                case JsonValueKind.Array:
                case JsonValueKind.Object:
                    properties.Add(new IdentityProperty(key, value.GetRawText()));
                    return;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return;

                default:
                    properties.Add(new IdentityProperty(key, value.ToString()));
                    return;
            }
        }

        private static string Base64UrlEncode(byte[] bytes) {
            return Convert.ToBase64String(bytes)
                          .TrimEnd('=')
                          .Replace('+', '-')
                          .Replace('/', '_');
        }

        private static byte[] Base64UrlDecode(string input) {
            string s = input.Replace('-', '+').Replace('_', '/');

            switch (s.Length % 4) {
                case 0:
                    break;
                case 2:
                    s += "==";
                    break;
                case 3:
                    s += "=";
                    break;
                default:
                    throw new FormatException("Invalid Base64Url length.");
            }

            return Convert.FromBase64String(s);
        }

        #endregion helpers

        #region header payload

        private sealed class JwtHeader {
            [JsonPropertyName("alg")]
            public string? Alg { get; set; }

            [JsonPropertyName("typ")]
            public string? Typ { get; set; }
        }

        private sealed class JwtPayload {
            [JsonPropertyName("sub")]
            public string? Sub { get; set; }

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("email")]
            public string? Email { get; set; }

            [JsonPropertyName("iss")]
            public string? Iss { get; set; }

            [JsonPropertyName("aud")]
            public JsonElement Aud { get; set; }

            [JsonPropertyName("exp")]
            public long? Exp { get; set; }

            [JsonPropertyName("nbf")]
            public long? Nbf { get; set; }

            [JsonPropertyName("iat")]
            public long? Iat { get; set; }

            [JsonExtensionData]
            public Dictionary<string, JsonElement> Extra { get; set; } = new(StringComparer.Ordinal);
        }

        #endregion header payload

    }

}
