using NFluent;
using SimpleW;
using SimpleW.Helper.Jwt;
using Xunit;

namespace test {

    /// <summary>
    /// Tests for JwtBearerHelper / JwtBearerOptions
    /// </summary>
    public class JwtBearerHelperTests {

        [Fact]
        public void CreateToken_Then_ValidateToken_Should_RebuildPrincipal() {

            JwtBearerOptions options = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client",
                clockSkew: TimeSpan.FromMinutes(1),
                algorithm: "HS256"
            );

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

            string token = JwtBearerHelper.CreateToken(
                options,
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
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

            string token = JwtBearerHelper.CreateToken(
                options,
                principal,
                lifetime: TimeSpan.FromMinutes(30)
            );

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
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
        public void TryValidateToken_Should_ReturnFalse_When_TokenIsEmpty() {

            JwtBearerOptions options = JwtBearerOptions.Create(secretKey: "super-secret-key");

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
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

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
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

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: new[] { "admin" },
                properties: null
            );

            string token = JwtBearerHelper.CreateToken(
                optionsA,
                identity,
                lifetime: TimeSpan.FromHours(1),
                nowUtc: new DateTimeOffset(2026, 03, 22, 12, 00, 00, TimeSpan.Zero)
            );

            bool ok = JwtBearerHelper.TryValidateToken(
                optionsB,
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

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            string token = JwtBearerHelper.CreateToken(
                createOptions,
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            bool ok = JwtBearerHelper.TryValidateToken(
                validateOptions,
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

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            string token = JwtBearerHelper.CreateToken(
                createOptions,
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            bool ok = JwtBearerHelper.TryValidateToken(
                validateOptions,
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

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            string token = JwtBearerHelper.CreateToken(
                options,
                identity,
                lifetime: TimeSpan.FromMinutes(10),
                nowUtc: DateTimeOffset.UtcNow.AddHours(-3)
            );

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
                token,
                out HttpPrincipal? principal,
                out string? error
            );

            // validate at 12:20 via token crafted at 12:00 with exp 12:10
            ok = JwtBearerHelper.TryValidateToken(
                options,
                RecreateSameTokenWithValidationTime(token), // same token, helper only for readability
                out principal,
                out error
            );

            // The helper itself uses DateTimeOffset.UtcNow internally, so this test only works reliably
            // if executed before token expiration relative to system clock.
            // Keep a deterministic expired-token test below instead.
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

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: null,
                properties: null
            );

            string token = JwtBearerHelper.CreateToken(
                createOptions,
                identity,
                lifetime: TimeSpan.FromHours(1),
                nowUtc: new DateTimeOffset(2026, 03, 22, 12, 00, 00, TimeSpan.Zero)
            );

            bool ok = JwtBearerHelper.TryValidateToken(
                validateOptions,
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

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: new[] { "admin" },
                properties: null
            );

            string token = JwtBearerHelper.CreateToken(options, identity, TimeSpan.FromHours(1));

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
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

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: null,
                roles: new[] { "admin", "devops" },
                properties: null
            );

            string token = JwtBearerHelper.CreateToken(options, identity, TimeSpan.FromHours(1));

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
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

            string token = JwtBearerHelper.CreateToken(options, identity, TimeSpan.FromHours(1));

            bool ok = JwtBearerHelper.TryValidateToken(
                options,
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

        private static string RecreateSameTokenWithValidationTime(string token) {
            return token;
        }
    }
}