using System.Security.Claims;


namespace SimpleW.Helper.OpenID {

    /// <summary>
    /// Context passed to principal factories.
    /// </summary>
    public class OpenIDPrincipalContext {

        #region properties

        /// <summary>
        /// Current session.
        /// </summary>
        public required HttpSession Session { get; init; }

        /// <summary>
        /// Provider logical name.
        /// </summary>
        public required string ProviderName { get; init; }

        /// <summary>
        /// Provider configuration.
        /// </summary>
        public required OpenIDProviderOptions Provider { get; init; }

        /// <summary>
        /// Validated claims principal from the id_token.
        /// </summary>
        public required ClaimsPrincipal ClaimsPrincipal { get; init; }

        /// <summary>
        /// Authentication time in UTC.
        /// </summary>
        public required DateTimeOffset AuthenticatedAt { get; init; }

        #endregion properties

    }

}
