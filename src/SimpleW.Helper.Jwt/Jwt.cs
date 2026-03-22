using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;


namespace SimpleW.Helper.Jwt {

    /// <summary>
    /// JWT Bearer options
    /// </summary>
    public sealed class JwtBearerOptions {

        /// <summary>
        /// Precomputed Shared secret key bytes used to validate and create HMAC JWT tokens
        /// </summary>
        internal byte[] SecretKeyBytes { get; private set; } = Array.Empty<byte>();

        /// <summary>
        /// Expected issuer (iss). Null = do not validate issuer.
        /// </summary>
        public string? ExpectedIssuer { get; init; }

        /// <summary>
        /// Expected audience (aud). Null = do not validate audience.
        /// </summary>
        public string? ExpectedAudience { get; init; }

        /// <summary>
        /// Allowed clock skew for exp / nbf validation (default 1 minute)
        /// </summary>
        public TimeSpan ClockSkew { get; init; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// JWT HMAC algorithm: HS256 / HS384 / HS512 (default HS256)
        /// </summary>
        public string Algorithm { get; init; } = "HS256";

        /// <summary>
        /// Private Constructor
        /// </summary>
        /// <param name="secretKey"></param>
        /// <param name="issuer"></param>
        /// <param name="audience"></param>
        /// <param name="clockSkew"></param>
        /// <param name="algorithm"></param>
        /// <exception cref="ArgumentException"></exception>
        private JwtBearerOptions(
            string secretKey,
            string? issuer,
            string? audience,
            TimeSpan clockSkew,
            string algorithm
        ) {
            if (string.IsNullOrWhiteSpace(secretKey)) {
                throw new ArgumentException("SecretKey required");
            }

            algorithm = (algorithm ?? "").Trim().ToUpperInvariant();

            if (algorithm is not ("HS256" or "HS384" or "HS512")) {
                throw new ArgumentException("Invalid algorithm");
            }

            if (clockSkew < TimeSpan.Zero) {
                throw new ArgumentException("ClockSkew must be >= 0");
            }

            ExpectedIssuer = string.IsNullOrWhiteSpace(issuer) ? null : issuer;
            ExpectedAudience = string.IsNullOrWhiteSpace(audience) ? null : audience;
            ClockSkew = clockSkew;
            Algorithm = algorithm;

            SecretKeyBytes = Encoding.UTF8.GetBytes(secretKey);
        }

        /// <summary>
        /// Builder
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
            return new JwtBearerOptions(
                secretKey,
                issuer,
                audience,
                clockSkew ?? TimeSpan.FromMinutes(1),
                algorithm
            );
        }

    }

    /// <summary>
    /// JWT Bearer helper
    /// </summary>
    public static class JwtBearerHelper {

        /// <summary>
        /// Create JWT from a principal
        /// </summary>
        /// <param name="options"></param>
        /// <param name="principal"></param>
        /// <param name="lifetime"></param>
        /// <param name="issuer"></param>
        /// <param name="audience"></param>
        /// <param name="nowUtc"></param>
        /// <returns></returns>
        public static string CreateToken(JwtBearerOptions options, HttpPrincipal principal, TimeSpan lifetime, string? issuer = null, string? audience = null, DateTimeOffset? nowUtc = null) {
            ArgumentNullException.ThrowIfNull(principal);
            return CreateToken(options, principal.Identity, lifetime, issuer, audience, nowUtc);
        }

        /// <summary>
        /// Create JWT from an identity
        /// </summary>
        /// <param name="options"></param>
        /// <param name="identity"></param>
        /// <param name="lifetime"></param>
        /// <param name="issuer"></param>
        /// <param name="audience"></param>
        /// <param name="nowUtc"></param>
        /// <returns></returns>
        public static string CreateToken(JwtBearerOptions options,HttpIdentity identity, TimeSpan lifetime, string? issuer = null, string? audience = null, DateTimeOffset? nowUtc = null) {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(identity);

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

            issuer ??= options.ExpectedIssuer;
            audience ??= options.ExpectedAudience;

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

                // keep explicit core mapping priority
                if (payload.ContainsKey(property.Key)) {
                    continue;
                }

                payload[property.Key] = property.Value;
            }

            Dictionary<string, object?> header = new(StringComparer.Ordinal) {
                ["alg"] = options.Algorithm,
                ["typ"] = "JWT"
            };

            string encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
            string encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
            string signingInput = encodedHeader + "." + encodedPayload;

            byte[] signature = ComputeSignature(options.Algorithm, Encoding.ASCII.GetBytes(signingInput), options.SecretKeyBytes);

            return signingInput + "." + Base64UrlEncode(signature);
        }

        /// <summary>
        /// Validate JWT token and rebuild a principal
        /// </summary>
        /// <param name="options"></param>
        /// <param name="token"></param>
        /// <param name="principal"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public static bool TryValidateToken(JwtBearerOptions options, string token, out HttpPrincipal? principal, out string? error) {
            ArgumentNullException.ThrowIfNull(options);

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

            if (!string.Equals(header.Alg, options.Algorithm, StringComparison.Ordinal)) {
                error = "JWT algorithm is invalid.";
                return false;
            }

            if (!TryVerifySignature(header.Alg, encodedHeader, encodedPayload, signatureBytes, options.SecretKeyBytes)) {
                error = "JWT signature is invalid.";
                return false;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (payload.Exp.HasValue) {
                DateTimeOffset exp = DateTimeOffset.FromUnixTimeSeconds(payload.Exp.Value);
                if (now > exp + options.ClockSkew) {
                    error = "JWT token is expired.";
                    return false;
                }
            }

            if (payload.Nbf.HasValue) {
                DateTimeOffset nbf = DateTimeOffset.FromUnixTimeSeconds(payload.Nbf.Value);
                if (now + options.ClockSkew < nbf) {
                    error = "JWT token is not active yet.";
                    return false;
                }
            }

            if (options.ExpectedIssuer != null
                && !string.Equals(payload.Iss, options.ExpectedIssuer, StringComparison.Ordinal)
            ) {
                error = "JWT issuer is invalid.";
                return false;
            }

            if (options.ExpectedAudience != null) {
                bool audienceMatch = false;

                if (payload.Aud.ValueKind == JsonValueKind.String) {
                    audienceMatch = string.Equals(payload.Aud.GetString(), options.ExpectedAudience, StringComparison.Ordinal);
                }
                else if (payload.Aud.ValueKind == JsonValueKind.Array) {
                    foreach (JsonElement element in payload.Aud.EnumerateArray()) {
                        if (element.ValueKind == JsonValueKind.String &&
                            string.Equals(element.GetString(), options.ExpectedAudience, StringComparison.Ordinal)) {
                            audienceMatch = true;
                            break;
                        }
                    }
                }

                if (!audienceMatch) {
                    error = "JWT audience is invalid.";
                    return false;
                }
            }

            principal = BuildPrincipal(payload);
            return true;
        }

        #region helpers

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

        private static HttpPrincipal BuildPrincipal(JwtPayload payload) {
            List<string> roles = new();
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
                        break;

                    case "role":
                    case "roles":
                        AddRoles(roles, value);
                        break;

                    default:
                        AddProperty(properties, key, value);
                        break;
                }
            }

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: payload.Sub,
                name: payload.Name,
                email: payload.Email,
                roles: roles,
                properties: properties
            );

            return new HttpPrincipal(identity);
        }

        private static void AddRoles(List<string> roles, JsonElement value) {
            if (value.ValueKind == JsonValueKind.String) {
                string? role = value.GetString();
                if (!string.IsNullOrWhiteSpace(role)) {
                    roles.Add(role);
                }
                return;
            }

            if (value.ValueKind == JsonValueKind.Array) {
                foreach (JsonElement item in value.EnumerateArray()) {
                    if (item.ValueKind == JsonValueKind.String) {
                        string? role = item.GetString();
                        if (!string.IsNullOrWhiteSpace(role)) {
                            roles.Add(role);
                        }
                    }
                }
            }
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