using NFluent;
using SimpleW;
using Xunit;

namespace test {

    /// <summary>
    /// Tests for HttpPrincipal / HttpIdentity / IdentityProperty
    /// </summary>
    public class PrincipalTests {

        [Fact]
        public void Principal_Anonymous_DefaultValues() {

            HttpPrincipal principal = HttpPrincipal.Anonymous;

            Check.That(principal).IsNotNull();
            Check.That(principal.Identity).IsNotNull();
            Check.That(principal.IsAuthenticated).IsFalse();
            Check.That(principal.Name).IsNull();
            Check.That(principal.Email).IsNull();
            Check.That(principal.Roles).IsNotNull();
            Check.That(principal.Roles).IsEmpty();
        }

        [Fact]
        public void Principal_FromIdentity_ExposeMainProperties() {

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "owner",
                email: "owner@simplew.dev",
                roles: new[] { "admin", "devops" },
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew"),
                    new IdentityProperty("plan", "pro")
                }
            );

            HttpPrincipal principal = new(identity);

            Check.That(principal.Identity).IsSameReferenceAs(identity);
            Check.That(principal.IsAuthenticated).IsTrue();
            Check.That(principal.Name).IsEqualTo("owner");
            Check.That(principal.Email).IsEqualTo("owner@simplew.dev");
            Check.That(principal.Roles).Contains("admin");
            Check.That(principal.Roles).Contains("devops");
            Check.That(principal.Roles.Count).IsEqualTo(2);
        }

        [Fact]
        public void Principal_IsInRole() {

            HttpPrincipal principal = new(new HttpIdentity(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "owner",
                email: null,
                roles: new[] { "admin", "devops" },
                properties: null
            ));

            Check.That(principal.IsInRole("admin")).IsTrue();            // role matches
            Check.That(principal.IsInRole("devops")).IsTrue();           // role matches
            Check.That(principal.IsInRole("Admin")).IsFalse();           // case-sensitive by design
            Check.That(principal.IsInRoles("user,admin")).IsTrue();      // at least one role matches
            Check.That(principal.IsInRoles("manager, devops")).IsTrue(); // at least one role matches
            Check.That(principal.IsInRole("user")).IsFalse();            // no role matches
            Check.That(principal.IsInRoles("user,manager")).IsFalse();   // no role matches
            Check.That(principal.IsInRoles("")).IsFalse();               // no role matches
            Check.That(principal.IsInRoles("   ")).IsFalse();            // no role matches
        }

        [Fact]
        public void Identity_Roles_Empty_WhenNullRolesProvided() {

            HttpIdentity identity = new(
                isAuthenticated: false,
                authenticationType: null,
                identifier: null,
                name: null,
                email: null,
                roles: null,
                properties: null
            );

            Check.That(identity.Roles).IsNotNull();
            Check.That(identity.Roles).IsEmpty();
        }

        [Fact]
        public void Identity_Get_ReturnsPropertyValue() {

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "owner",
                email: null,
                roles: null,
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew"),
                    new IdentityProperty("plan", "pro")
                }
            );

            Check.That(identity.Get("tenant_id")).IsEqualTo("simplew");
            Check.That(identity.Get("plan")).IsEqualTo("pro");
            Check.That(identity.Get("does_not_exist")).IsNull();
        }

        [Fact]
        public void Principal_Get_ReturnsPropertyValue_FromPrincipal() {

            HttpPrincipal principal = new(new HttpIdentity(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "owner",
                email: null,
                roles: null,
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew"),
                    new IdentityProperty("locale", "fr-FR")
                }
            ));

            Check.That(principal.Get("tenant_id")).IsEqualTo("simplew");
            Check.That(principal.Get("locale")).IsEqualTo("fr-FR");
        }

        [Fact]
        public void Identity_Has() {

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "owner",
                email: null,
                roles: null,
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew"),
                    new IdentityProperty("plan", "pro")
                }
            );

            Check.That(identity.Has("tenant_id")).IsTrue();
            Check.That(identity.Has("plan")).IsTrue();
            Check.That(identity.Has("tenant_id", "simplew")).IsTrue();
            Check.That(identity.Has("plan", "pro")).IsTrue();
            Check.That(identity.Has("tenant_id", "other")).IsFalse();
        }


        [Fact]
        public void Identity_Properties_Empty_WhenNullPropertiesProvided() {

            HttpIdentity identity = new(
                isAuthenticated: false,
                authenticationType: null,
                identifier: null,
                name: null,
                email: null,
                roles: null,
                properties: null
            );

            Check.That(identity.Properties).IsNotNull();
            Check.That(identity.Properties).IsEmpty();
        }

        [Fact]
        public void Identity_IsAuthenticated_And_AuthenticationType_ArePreserved() {

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "owner",
                email: null,
                roles: null,
                properties: null
            );

            Check.That(identity.IsAuthenticated).IsTrue();
            Check.That(identity.AuthenticationType).IsEqualTo("Bearer");
        }

    }

}