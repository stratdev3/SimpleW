using System.Security.Cryptography;
using System.Text;


namespace SimpleW.Security {

    /// <summary>
    /// Jwt
    /// </summary>
    public static class Jwt {

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// Encode
        /// </summary>
        /// <param name="jsonEngine"></param>
        /// <param name="standard"></param>
        /// <param name="customClaims"></param>
        /// <param name="secret"></param>
        /// <param name="header"></param>
        /// <returns></returns>
        public static string EncodeHs256(IJsonEngine jsonEngine, JwtTokenPayload standard, IReadOnlyDictionary<string, object?> customClaims, byte[] secret, object? header = null) {
            header ??= new { alg = "HS256", typ = "JWT" };

            // standard to dict
            Dictionary<string, object?> payload = standard.ToDict();
            // custom to dict
            foreach (var kv in customClaims) {
                if (payload.ContainsKey(kv.Key)) {
                    throw new ArgumentException($"Custom claim '{kv.Key}' conflicts with a registered claim.", nameof(customClaims));
                }
                payload[kv.Key] = kv.Value;
            }

            string headerJson = jsonEngine.Serialize(header);
            string payloadJson = jsonEngine.Serialize(payload);

            string h64 = Base64UrlEncode(Utf8.GetBytes(headerJson));
            string p64 = Base64UrlEncode(Utf8.GetBytes(payloadJson));

            string signingInput = $"{h64}.{p64}";
            string sig64 = Base64UrlEncode(HmacSha256(Utf8.GetBytes(signingInput), secret));

            return $"{signingInput}.{sig64}";
        }

        /// <summary>
        /// Encode
        /// </summary>
        /// <param name="jsonEngine"></param>
        /// <param name="standard"></param>
        /// <param name="customClaims"></param>
        /// <param name="secret"></param>
        /// <param name="header"></param>
        /// <returns></returns>
        public static string EncodeHs256(IJsonEngine jsonEngine, JwtTokenPayload standard, IReadOnlyDictionary<string, object?> customClaims, string secret, object? header = null) {
            return EncodeHs256(jsonEngine, standard, customClaims, Utf8.GetBytes(secret), header);
        }

        /// <summary>
        /// Decode
        /// </summary>
        /// <param name="jsonEngine"></param>
        /// <param name="token"></param>
        /// <param name="options"></param>
        /// <param name="jwt"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        public static bool TryDecodeAndValidate(IJsonEngine jsonEngine, string token, JwtOptions options, out JwtToken? jwt, out JwtError error) {
            jwt = null;
            error = JwtError.None;

            if (string.IsNullOrWhiteSpace(token)) {
                error = JwtError.InvalidFormat;
                return false;
            }

            int p1 = token.IndexOf('.');
            if (p1 <= 0) { error = JwtError.InvalidFormat; return false; }
            int p2 = token.IndexOf('.', p1 + 1);
            if (p2 <= p1 + 1 || p2 >= token.Length - 1) { error = JwtError.InvalidFormat; return false; }

            string h64 = token.Substring(0, p1);
            string p64 = token.Substring(p1 + 1, p2 - (p1 + 1));
            string s64 = token.Substring(p2 + 1);

            byte[] headerBytes, payloadBytes;
            string payloadJson;
            try {
                headerBytes = Base64UrlDecode(h64);
                payloadBytes = Base64UrlDecode(p64);
                payloadJson = Utf8.GetString(payloadBytes);
            }
            catch {
                error = JwtError.InvalidBase64;
                return false;
            }

            JwtTokenHeader header;
            JwtTokenPayload payload;
            try {
                header = jsonEngine.Deserialize<JwtTokenHeader>(Utf8.GetString(headerBytes));
                payload = jsonEngine.Deserialize<JwtTokenPayload>(payloadJson);
            }
            catch {
                error = JwtError.InvalidJson;
                return false;
            }

            // alg check
            if (!string.Equals(header.alg, "HS256", StringComparison.Ordinal)) {
                error = JwtError.UnsupportedAlg;
                return false;
            }

            // signature check (constant time compare)
            string signingInput = $"{h64}.{p64}";
            byte[] expectedSig = HmacSha256(Utf8.GetBytes(signingInput), options.Key);

            byte[] providedSig;
            try {
                providedSig = Base64UrlDecode(s64);
            }
            catch {
                error = JwtError.InvalidBase64;
                return false;
            }

            if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig)) {
                error = JwtError.BadSignature;
                return false;
            }

            
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // exp validation (epoch seconds)
            if (options.ValidateExp && now - (long)options.ClockSkew.TotalSeconds > payload.exp) {
                error = JwtError.Expired;
                return false;
            }

            // nbf validation (epoch seconds)
            if (options.ValidateNbf && (now + (long)options.ClockSkew.TotalSeconds < payload.nbf)) {
                error = JwtError.NotYetValid;
                return false;
            }

            // iss validation
            if (options.ValidateIss && !string.IsNullOrWhiteSpace(options.ValidIss) && options.ValidIss != payload.iss) {
                error = JwtError.InvalidIssuer;
                return false;
            }

            jwt = new JwtToken(header, payload, payloadJson, s64);
            return true;
        }

        #region helpers

        /// <summary>
        /// HmacSha256
        /// </summary>
        /// <param name="data"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        private static byte[] HmacSha256(byte[] data, byte[] key) {
            using HMACSHA256 h = new(key);
            return h.ComputeHash(data);
        }

        /// <summary>
        /// Base64UrlEncode
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        private static string Base64UrlEncode(byte[] bytes) {
            string b64 = Convert.ToBase64String(bytes);
            return b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// Base64UrlDecode
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        /// <exception cref="FormatException"></exception>
        private static byte[] Base64UrlDecode(string s) {
            string b64 = s.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4) {
                case 0:
                    break;
                case 2:
                    b64 += "==";
                    break;
                case 3:
                    b64 += "=";
                    break;
                default:
                    throw new FormatException("Invalid base64url length.");
            }
            return Convert.FromBase64String(b64);
        }

        #endregion helpers

    }

    /// <summary>
    /// JwtOptions
    /// </summary>
    public sealed class JwtOptions {

        /// <summary>
        /// the secret bytes (HS256)
        /// </summary>
        public byte[] Key { get; init; }

        /// <summary>
        /// Time Discrepency
        /// </summary>
        public TimeSpan ClockSkew { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Valid Issuer
        /// </summary>
        public string ValidIss { get; init; } = string.Empty;

        /// <summary>
        /// Validate the Exp (Expire) Claim
        /// </summary>
        public bool ValidateExp { get; init; } = true;

        /// <summary>
        /// Validate the Iss (Issuer) Claim
        /// </summary>
        public bool ValidateIss { get; init; } = false;

        /// <summary>
        /// Validate the Nbf (Not Before) Claim
        /// </summary>
        public bool ValidateNbf { get; init; } = true;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="key"></param>
        public JwtOptions(string key) {
            Key = Encoding.UTF8.GetBytes(key);
        }

    }

    /// <summary>
    /// JwtError
    /// </summary>
    public enum JwtError {
        /// <summary>
        /// No error
        /// </summary>
        None,
        /// <summary>
        /// Invalid Json
        /// </summary>
        InvalidJsonOptions,
        /// <summary>
        /// Invalid Format
        /// </summary>
        InvalidFormat,
        /// <summary>
        /// Invalid Raw Token
        /// </summary>
        InvalidBase64,
        /// <summary>
        /// Invalid json
        /// </summary>
        InvalidJson,
        /// <summary>
        /// Unsupported Algo
        /// </summary>
        UnsupportedAlg,
        /// <summary>
        /// Wrong Signature
        /// </summary>
        BadSignature,
        /// <summary>
        /// Token Expired
        /// </summary>
        Expired,
        /// <summary>
        /// Invalid Issuer
        /// </summary>
        InvalidIssuer,
        /// <summary>
        /// Token Before Valid
        /// </summary>
        NotYetValid,
    }

    /// <summary>
    /// JwtToken
    /// </summary>
    /// <param name="Header"></param>
    /// <param name="Payload"></param>
    /// <param name="RawPayload"></param>
    /// <param name="SignatureB64Url"></param>
    public sealed record JwtToken(
        JwtTokenHeader Header,
        JwtTokenPayload Payload,
        string RawPayload,
        string SignatureB64Url
    );

    /// <summary>
    /// JwtHeader
    /// </summary>
    public class JwtTokenHeader {

        /// <summary>
        /// Algo
        /// </summary>
        public string alg { get; set; } = string.Empty;

        /// <summary>
        /// Type
        /// </summary>
        public string typ { get; set; } = "JWT";
    }

    /// <summary>
    /// JwtPayload
    /// </summary>
    public class JwtTokenPayload {

        /// <summary>
        /// Expire
        /// </summary>
        public long exp { get; set; }

        /// <summary>
        /// Not Before
        /// </summary>
        public long nbf { get; set; }

        /// <summary>
        /// Issue At
        /// </summary>
        public long iat { get; set; }

        /// <summary>
        /// Issuer
        /// </summary>
        public string iss { get; set; } = string.Empty;

        /// <summary>
        /// Subject
        /// </summary>
        public string sub { get; set; } = string.Empty;

        /// <summary>
        /// Audiance
        /// </summary>
        public string aud { get; set; } = string.Empty;

        /// <summary>
        /// Create
        /// </summary>
        /// <param name="expiration"></param>
        /// <param name="issuer"></param>
        public static JwtTokenPayload Create(TimeSpan expiration, string issuer = "") {
            return new JwtTokenPayload() {
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = DateTimeOffset.UtcNow.Add(expiration).ToUnixTimeSeconds(),
                iss = issuer,
            };
        }

        /// <summary>
        /// Create
        /// </summary>
        /// <param name="expire">should be utc</param>
        /// <param name="issuer"></param>
        /// <returns></returns>
        public static JwtTokenPayload Create(DateTime expire, string issuer = "") {
            return new JwtTokenPayload() {
                iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                exp = new DateTimeOffset(expire).ToUnixTimeSeconds(),
                iss = issuer,
            };
        }

        /// <summary>
        /// Convert To Dictionary
        /// </summary>
        /// <returns></returns>
        public Dictionary<string, object?> ToDict() {
            Dictionary<string, object?> payload = new(StringComparer.Ordinal) {
                ["exp"] = exp,
            };
            if (nbf != 0) {
                payload.Add("nbf", nbf);
            }
            if (iat != 0) {
                payload.Add("iat", iat);
            }
            if (!string.IsNullOrWhiteSpace(iss)) {
                payload.Add("iss", iss);
            }
            if (!string.IsNullOrWhiteSpace(sub)) {
                payload.Add("sub", sub);
            }
            if (!string.IsNullOrWhiteSpace(aud)) {
                payload.Add("aud", aud);
            }

            return payload;
        }

    }

}
