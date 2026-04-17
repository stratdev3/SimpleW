using NFluent;
using SimpleW;
using SimpleW.Helper.Jwt;
using Xunit;

namespace test {

    /// <summary>
    /// Tests for JwtBearerHelper / JwtBearerOptions.
    /// </summary>
    public class JwtBearerHelperTests {

        [Fact]
        public void CreateToken_Then_ValidateToken_WithHelperInstance_Should_RebuildPrincipal() {

            JwtBearerOptions options = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client",
                clockSkew: TimeSpan.FromMinutes(1),
                algorithm: "HS256"
            );

            JwtBearerHelper helper = new(options);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: "owner@simplew.dev",
                roles: new[] { "admin", "devops" },
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew"),
                    new IdentityProperty("plan", "pro")
                }
            );

            string token = helper.CreateToken(
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            bool ok = helper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsTrue();
            Check.That(error).IsNull();
            Check.That(principal).IsNotNull();

            Check.That(principal!.IsAuthenticated).IsTrue();
            Check.That(principal.Identity.AuthenticationType).IsEqualTo("Bearer");
            Check.That(principal.Identity.Identifier).IsEqualTo("owner");
            Check.That(principal.Name).IsEqualTo("Owner");
            Check.That(principal.Email).IsEqualTo("owner@simplew.dev");

            Check.That(principal.IsInRole("admin")).IsTrue();
            Check.That(principal.IsInRole("devops")).IsTrue();
            Check.That(principal.IsInRole("Admin")).IsFalse(); // case-sensitive by design

            Check.That(principal.Get("login")).IsEqualTo("Owner");
            Check.That(principal.Has("auth_scheme", "Bearer")).IsTrue();
            Check.That(principal.Has("issuer", "SimpleW")).IsTrue();
            Check.That(principal.Has("audience", "SimpleW.Client")).IsTrue();
            Check.That(principal.Get("auth_time")).IsNotNull();
            Check.That(principal.Get("tenant_id")).IsEqualTo("simplew");
            Check.That(principal.Get("plan")).IsEqualTo("pro");
        }

        [Fact]
        public void CreateToken_FromPrincipal_Should_Validate() {

            JwtBearerOptions options = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            JwtBearerHelper helper = new(options);

            HttpPrincipal principal = new(new HttpIdentity(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "u42",
                name: "Owner",
                email: null,
                roles: new[] { "admin" },
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew")
                }
            ));

            string token = helper.CreateToken(
                principal,
                lifetime: TimeSpan.FromMinutes(30)
            );

            bool ok = helper.TryValidateToken(
                token,
                out HttpPrincipal? validated,
                out string? error
            );

            Check.That(ok).IsTrue();
            Check.That(error).IsNull();
            Check.That(validated).IsNotNull();
            Check.That(validated!.Identity.Identifier).IsEqualTo("u42");
            Check.That(validated.IsInRole("admin")).IsTrue();
            Check.That(validated.Get("tenant_id")).IsEqualTo("simplew");
        }

        [Fact]
        public void TryValidateToken_Should_UseCustomPrincipalFactory() {

            JwtBearerOptions options = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            options.AuthenticationType = "Jwt";
            options.PrincipalFactory = context => new HttpPrincipal(new HttpIdentity(
                isAuthenticated: true,
                authenticationType: "CustomBearer",
                identifier: context.Subject,
                name: context.Name,
                email: context.Email,
                roles: context.Roles,
                properties: new[] {
                    new IdentityProperty("factory", "custom"),
                    new IdentityProperty("token_subject", context.Subject ?? string.Empty)
                }
            ));

            JwtBearerHelper helper = new(options);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: "owner@simplew.dev",
                roles: new[] { "admin" },
                properties: null
            );

            string token = helper.CreateToken(identity, TimeSpan.FromHours(1));

            bool ok = helper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsTrue();
            Check.That(error).IsNull();
            Check.That(principal).IsNotNull();
            Check.That(principal!.Identity.AuthenticationType).IsEqualTo("CustomBearer");
            Check.That(principal.Get("factory")).IsEqualTo("custom");
            Check.That(principal.Get("token_subject")).IsEqualTo("owner");
        }

        [Fact]
        public void TryValidateToken_Should_ReturnFalse_When_TokenIsEmpty() {

            JwtBearerOptions options = JwtBearerOptions.Create(secretKey: "super-secret-key");
            JwtBearerHelper helper = new(options);

            bool ok = helper.TryValidateToken(
                "",
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsFalse();
            Check.That(principal).IsNull();
            Check.That(error).IsEqualTo("JWT token is empty.");
        }

        [Fact]
        public void TryValidateToken_Should_ReturnFalse_When_TokenDoesNotHave3Parts() {

            JwtBearerOptions options = JwtBearerOptions.Create(secretKey: "super-secret-key");
            JwtBearerHelper helper = new(options);

            bool ok = helper.TryValidateToken(
                "abc.def",
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsFalse();
            Check.That(principal).IsNull();
            Check.That(error).IsEqualTo("JWT must contain exactly 3 parts.");
        }

        [Fact]
        public void TryValidateToken_Should_ReturnFalse_When_SignatureIsInvalid() {

            JwtBearerOptions optionsA = JwtBearerOptions.Create(
                secretKey: "super-secret-key-a",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            JwtBearerOptions optionsB = JwtBearerOptions.Create(
                secretKey: "super-secret-key-b",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            JwtBearerHelper helperA = new(optionsA);
            JwtBearerHelper helperB = new(optionsB);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: new[] { "admin" },
                properties: null
            );

            string token = helperA.CreateToken(
                identity,
                lifetime: TimeSpan.FromHours(1),
                nowUtc: new DateTimeOffset(2026, 03, 22, 12, 00, 00, TimeSpan.Zero)
            );

            bool ok = helperB.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsFalse();
            Check.That(principal).IsNull();
            Check.That(error).IsEqualTo("JWT signature is invalid.");
        }

        [Fact]
        public void TryValidateToken_Should_ReturnFalse_When_IssuerIsInvalid() {

            JwtBearerOptions createOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            JwtBearerOptions validateOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "OtherIssuer",
                audience: "SimpleW.Client"
            );

            JwtBearerHelper createHelper = new(createOptions);
            JwtBearerHelper validateHelper = new(validateOptions);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            string token = createHelper.CreateToken(
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            bool ok = validateHelper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsFalse();
            Check.That(principal).IsNull();
            Check.That(error).IsEqualTo("JWT issuer is invalid.");
        }

        [Fact]
        public void TryValidateToken_Should_ReturnFalse_When_AudienceIsInvalid() {

            JwtBearerOptions createOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            JwtBearerOptions validateOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "OtherAudience"
            );

            JwtBearerHelper createHelper = new(createOptions);
            JwtBearerHelper validateHelper = new(validateOptions);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            string token = createHelper.CreateToken(
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            bool ok = validateHelper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsFalse();
            Check.That(principal).IsNull();
            Check.That(error).IsEqualTo("JWT audience is invalid.");
        }

        [Fact]
        public void TryValidateToken_Should_ReturnFalse_When_TokenIsExpired() {

            JwtBearerOptions options = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client",
                clockSkew: TimeSpan.Zero
            );

            JwtBearerHelper helper = new(options);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            DateTimeOffset issuedAt = new(2026, 03, 22, 12, 00, 00, TimeSpan.Zero);

            string token = helper.CreateToken(
                identity,
                lifetime: TimeSpan.FromMinutes(10),
                nowUtc: issuedAt
            );

            bool ok = helper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error,
                nowUtc: issuedAt.AddMinutes(20)
            );

            Check.That(ok).IsFalse();
            Check.That(principal).IsNull();
            Check.That(error).IsEqualTo("JWT token is expired.");
        }

        [Fact]
        public void TryValidateToken_Should_ReturnFalse_When_AlgorithmDoesNotMatch() {

            JwtBearerOptions createOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client",
                algorithm: "HS256"
            );

            JwtBearerOptions validateOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client",
                algorithm: "HS512"
            );

            JwtBearerHelper createHelper = new(createOptions);
            JwtBearerHelper validateHelper = new(validateOptions);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            string token = createHelper.CreateToken(
                identity,
                lifetime: TimeSpan.FromHours(1),
                nowUtc: new DateTimeOffset(2026, 03, 22, 12, 00, 00, TimeSpan.Zero)
            );

            bool ok = validateHelper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsFalse();
            Check.That(principal).IsNull();
            Check.That(error).IsEqualTo("JWT algorithm is invalid.");
        }

        [Fact]
        public void CreateToken_Should_WriteSingleRole_AsRole() {

            JwtBearerOptions options = JwtBearerOptions.Create(secretKey: "super-secret-key");
            JwtBearerHelper helper = new(options);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: new[] { "admin" },
                properties: null
            );

            string token = helper.CreateToken(identity, TimeSpan.FromHours(1));

            bool ok = helper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsTrue();
            Check.That(error).IsNull();
            Check.That(principal).IsNotNull();
            Check.That(principal!.Roles.Count).IsEqualTo(1);
            Check.That(principal.IsInRole("admin")).IsTrue();
        }

        [Fact]
        public void CreateToken_Should_WriteMultipleRoles_AsRolesArray() {

            JwtBearerOptions options = JwtBearerOptions.Create(secretKey: "super-secret-key");
            JwtBearerHelper helper = new(options);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: new[] { "admin", "devops" },
                properties: null
            );

            string token = helper.CreateToken(identity, TimeSpan.FromHours(1));

            bool ok = helper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsTrue();
            Check.That(error).IsNull();
            Check.That(principal).IsNotNull();
            Check.That(principal!.IsInRole("admin")).IsTrue();
            Check.That(principal.IsInRole("devops")).IsTrue();
        }

        [Fact]
        public void CreateToken_Should_KeepCoreFieldsPriority_OverProperties() {

            JwtBearerOptions options = JwtBearerOptions.Create(secretKey: "super-secret-key");
            JwtBearerHelper helper = new(options);

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "real-sub",
                name: "real-name",
                email: "real@simplew.dev",
                roles: new[] { "admin" },
                properties: new[] {
                    new IdentityProperty("sub", "fake-sub"),
                    new IdentityProperty("name", "fake-name"),
                    new IdentityProperty("email", "fake@simplew.dev"),
                    new IdentityProperty("tenant_id", "simplew")
                }
            );

            string token = helper.CreateToken(identity, TimeSpan.FromHours(1));

            bool ok = helper.TryValidateToken(
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            Check.That(ok).IsTrue();
            Check.That(error).IsNull();
            Check.That(principal).IsNotNull();

            Check.That(principal!.Identity.Identifier).IsEqualTo("real-sub");
            Check.That(principal.Name).IsEqualTo("real-name");
            Check.That(principal.Email).IsEqualTo("real@simplew.dev");
            Check.That(principal.Get("tenant_id")).IsEqualTo("simplew");
        }

    }
}
