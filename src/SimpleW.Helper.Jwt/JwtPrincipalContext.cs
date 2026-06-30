namespace SimpleW.Helper.Jwt {

    /// <summary>
    /// Context passed to JWT principal factories.
    /// </summary>
    public sealed class JwtPrincipalContext {

        /// <summary>
        /// Current session when authentication originates from an HTTP request.
        /// </summary>
        public HttpSession? Session { get; init; }

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

    }

}
