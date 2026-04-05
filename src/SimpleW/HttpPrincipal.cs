using System.Data;

namespace SimpleW {

    /// <summary>
    /// Represents the current user principal.
    /// </summary>
    public sealed class HttpPrincipal {

        /// <summary>
        /// Represents an authenticated or anonymous identity.
        /// </summary>
        public HttpIdentity Identity { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpPrincipal"/> class.
        /// </summary>
        /// <param name="identity"></param>
        public HttpPrincipal(HttpIdentity identity) {
            Identity = identity;
        }

        /// <summary>
        /// IsAuthenticated
        /// </summary>
        public bool IsAuthenticated => Identity.IsAuthenticated;

        /// <summary>
        /// Name
        /// </summary>
        public string? Name => Identity.Name;

        /// <summary>
        /// Email
        /// </summary>
        public string? Email => Identity.Email;

        /// <summary>
        /// Roles
        /// </summary>
        public IReadOnlyCollection<string> Roles => Identity.Roles;

        /// <summary>
        /// IsInRole
        /// </summary>
        /// <param name="role"></param>
        /// <returns></returns>
        public bool IsInRole(string role) => Identity.IsInRole(role);

        /// <summary>
        /// IsInRoles
        /// </summary>
        /// <param name="roles"></param>
        /// <returns></returns>
        public bool IsInRoles(string roles) => Identity.IsInRoles(roles);

        /// <summary>
        /// Get a property
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string? Get(string key) => Identity.Get(key);

        /// <summary>
        /// Has
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool Has(string key, string? value = null) => Identity.Has(key, value);

        /// <summary>
        /// Anonymous Represents the current user principal.
        /// </summary>
        public static HttpPrincipal Anonymous { get; } = new(new HttpIdentity(false, null, null, null, null, null, null));

    }

    /// <summary>
    /// Represents an authenticated or anonymous identity.
    /// </summary>
    public sealed class HttpIdentity {

        /// <summary>
        /// IsAuthenticated
        /// </summary>
        public bool IsAuthenticated { get; }

        /// <summary>
        /// AuthenticationType
        /// </summary>
        public string? AuthenticationType { get; }

        /// <summary>
        /// Id
        /// </summary>
        public string? Identifier { get; }

        /// <summary>
        /// Name
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// Email
        /// </summary>
        public string? Email { get; }

        /// <summary>
        /// Roles
        /// </summary>
        private readonly HashSet<string> _roles;

        /// <summary>
        /// Roles
        /// </summary>
        public IReadOnlyCollection<string> Roles => _roles;

        /// <summary>
        /// Properties
        /// </summary>
        public IReadOnlyList<IdentityProperty> Properties { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpPrincipal"/> class.
        /// </summary>
        /// <param name="isAuthenticated"></param>
        /// <param name="authenticationType"></param>
        /// <param name="identifier"></param>
        /// <param name="name"></param>
        /// <param name="email"></param>
        /// <param name="roles"></param>
        /// <param name="properties"></param>
        public HttpIdentity(
            bool isAuthenticated,
            string? authenticationType,
            string? identifier,
            string? name,
            string? email,
            IEnumerable<string>? roles,
            IEnumerable<IdentityProperty>? properties
        ) {
            IsAuthenticated = isAuthenticated;
            AuthenticationType = authenticationType;

            Identifier = identifier;
            Name = name;
            Email = email;

            _roles = roles != null ? new(roles) : new();

            Properties = properties?.ToList() ?? new List<IdentityProperty>();
        }

        /// <summary>
        /// Returns <see langword="true"/> if the principal belongs to the specified role.
        /// </summary>
        /// <param name="role"></param>
        /// <returns></returns>
        public bool IsInRole(string role) => _roles.Contains(role);

        /// <summary>
        /// Returns <see langword="true"/> if the principal belongs to the specified role.
        /// </summary>
        /// <param name="roles"></param>
        /// <returns></returns>
        public bool IsInRoles(string roles) {
            if (string.IsNullOrWhiteSpace(roles)) {
                return false;
            }

            string[] parts = roles.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts) {
                if (IsInRole(part.Trim())) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Get a Property
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public string? Get(string key) => Properties.FirstOrDefault(p => p.Key == key)?.Value;

        /// <summary>
        /// Has
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool Has(string key, string? value = null) {
            foreach (var p in Properties) {
                if (p.Key != key) {
                    continue;
                }
                if (value == null || p.Value == value) {
                    return true;
                }
            }
            return false;
        }

    }

    /// <summary>
    /// Represents an authenticated or anonymous identity.Property
    /// </summary>
    public sealed class IdentityProperty {

        /// <summary>
        /// Key
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Value
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpPrincipal"/> class.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public IdentityProperty(string key, string value) {
            Key = key;
            Value = value;
        }

    }

}