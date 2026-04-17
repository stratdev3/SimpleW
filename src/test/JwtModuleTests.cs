using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.Helper.Jwt;
using SimpleW.Service.Jwt;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for JwtModule / JwtModuleOptions.
    /// </summary>
    public class JwtModuleTests {

        private const string SecretKey = "super-secret-key";
        private const string Issuer = "SimpleW";
        private const string Audience = "SimpleW.Api";

        [Fact]
        public async Task UseJwtModule_Should_Restore_Principal_And_Honor_AllowAnonymous() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseJwtModule(options => {
                options.SecretKey = SecretKey;
                options.Issuer = Issuer;
                options.Audience = Audience;
            });

            server.MapController<JwtModuleAccountController>("/api");

            await server.StartAsync();

            try {
                using HttpClient client = new();

                HttpResponseMessage publicResponse = await client.GetAsync($"http://{server.Address}:{server.Port}/api/account/public");
                string publicContent = await publicResponse.Content.ReadAsStringAsync();

                Check.That(publicResponse.StatusCode).Is(HttpStatusCode.OK);
                Check.That(publicContent).IsEqualTo(JsonSerializer.Serialize(new {
                    ok = true,
                    isAuthenticated = false
                }));

                using HttpRequestMessage meRequest = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/api/account/me");
                meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("user-1", "John", roles: [ "user" ]));

                HttpResponseMessage meResponse = await client.SendAsync(meRequest);
                string meContent = await meResponse.Content.ReadAsStringAsync();

                Check.That(meResponse.StatusCode).Is(HttpStatusCode.OK);
                Check.That(meContent).IsEqualTo(JsonSerializer.Serialize(new {
                    isAuthenticated = true,
                    id = "user-1",
                    name = "John",
                    authenticationType = "Bearer"
                }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseJwtModule_Should_Redirect_To_Login_When_Configured() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseJwtModule(options => {
                options.SecretKey = SecretKey;
                options.Issuer = Issuer;
                options.Audience = Audience;
                options.LoginUrl = "/auth/login";
            });

            server.MapController<JwtModuleAccountController>("/api");

            await server.StartAsync();

            try {
                using HttpClientHandler handler = new() {
                    AllowAutoRedirect = false
                };
                using HttpClient client = new(handler);

                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/account/me");

                Check.That(response.StatusCode).Is(HttpStatusCode.Found);
                Check.That(response.Headers.Location?.ToString()).IsEqualTo("/auth/login?returnUrl=%2Fapi%2Faccount%2Fme");
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseJwtModule_Should_Append_ReturnUrl_Before_LoginUrl_Fragment() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseJwtModule(options => {
                options.SecretKey = SecretKey;
                options.Issuer = Issuer;
                options.Audience = Audience;
                options.LoginUrl = "/auth/login?mode=sso#summary";
            });

            server.MapController<JwtModuleAccountController>("/api");

            await server.StartAsync();

            try {
                using HttpClientHandler handler = new() {
                    AllowAutoRedirect = false
                };
                using HttpClient client = new(handler);

                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/account/me");

                Check.That(response.StatusCode).Is(HttpStatusCode.Found);
                Check.That(response.Headers.Location?.ToString()).IsEqualTo("/auth/login?mode=sso&returnUrl=%2Fapi%2Faccount%2Fme#summary");
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseJwtModule_Should_Return401_Without_WwwAuthenticate_When_LoginUrl_Is_Not_Configured() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseJwtModule(options => {
                options.SecretKey = SecretKey;
                options.Issuer = Issuer;
                options.Audience = Audience;
            });

            server.MapController<JwtModuleAccountController>("/api");

            await server.StartAsync();

            try {
                using HttpClient client = new();

                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/account/me");
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.Unauthorized);
                Check.That(content).IsEmpty();
                Check.That(response.Headers.WwwAuthenticate.Any()).IsFalse();
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseJwtModule_Should_Enforce_Roles() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseJwtModule(options => {
                options.SecretKey = SecretKey;
                options.Issuer = Issuer;
                options.Audience = Audience;
                options.LoginUrl = "/auth/login";
            });

            server.MapController<JwtModuleAccountController>("/api");

            await server.StartAsync();

            try {
                using HttpClient client = new();

                using HttpRequestMessage forbiddenRequest = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/api/account/admin");
                forbiddenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("user-2", "Jane", roles: [ "user" ]));

                HttpResponseMessage forbiddenResponse = await client.SendAsync(forbiddenRequest);
                string forbiddenContent = await forbiddenResponse.Content.ReadAsStringAsync();

                Check.That(forbiddenResponse.StatusCode).Is(HttpStatusCode.Forbidden);
                Check.That(forbiddenContent).IsEqualTo(JsonSerializer.Serialize(new {
                    ok = false,
                    error = "forbidden",
                    role = "admin"
                }));

                using HttpRequestMessage successRequest = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/api/account/admin");
                successRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("admin-1", "Alice", roles: [ "admin" ]));

                HttpResponseMessage successResponse = await client.SendAsync(successRequest);
                string successContent = await successResponse.Content.ReadAsStringAsync();

                Check.That(successResponse.StatusCode).Is(HttpStatusCode.OK);
                Check.That(successContent).IsEqualTo(JsonSerializer.Serialize(new {
                    ok = true,
                    area = "admin"
                }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseJwtModule_Should_Use_ModulePrincipalFactory() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseJwtModule(options => {
                options.SecretKey = SecretKey;
                options.Issuer = Issuer;
                options.Audience = Audience;
                options.LoginUrl = "/auth/login";
                options.ReturnUrlParameterName = "next";
                options.ModulePrincipalFactory = context => new HttpPrincipal(new HttpIdentity(
                    isAuthenticated: true,
                    authenticationType: "JwtModule",
                    identifier: context.Subject,
                    name: context.Name,
                    email: context.Email,
                    roles: context.Roles,
                    properties: [
                        new IdentityProperty("source", "module"),
                        new IdentityProperty("login_url", context.LoginUrl ?? string.Empty),
                        new IdentityProperty("return_url_parameter", context.ReturnUrlParameterName ?? string.Empty),
                        new IdentityProperty("token_subject", context.Subject ?? string.Empty)
                    ]
                ));
            });

            server.MapController<JwtModuleCustomPrincipalController>("/api");

            await server.StartAsync();

            try {
                using HttpClient client = new();
                using HttpRequestMessage request = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/api/custom/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("partner-7", "Partner", roles: [ "partner" ]));

                HttpResponseMessage response = await client.SendAsync(request);
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new {
                    authenticationType = "JwtModule",
                    loginUrl = "/auth/login",
                    returnUrlParameter = "next",
                    source = "module",
                    tokenSubject = "partner-7"
                }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public void UseJwtModule_Should_Reject_Helper_With_Inline_Helper_Settings() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            JwtBearerHelper helper = CreateHelper();

            try {
                Assert.Throws<ArgumentException>(() => {
                    server.UseJwtModule(options => {
                        options.Helper = helper;
                        options.SecretKey = SecretKey;
                    });
                });
            }
            finally {
                PortManager.ReleasePort(server.Port);
            }
        }

        private static JwtBearerHelper CreateHelper() {
            return new JwtBearerHelper(options => {
                options.SecretKey = SecretKey;
                options.Issuer = Issuer;
                options.Audience = Audience;
            });
        }

        private static string CreateToken(string subject, string name, string[]? roles = null) {
            JwtBearerHelper helper = CreateHelper();

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: subject,
                name: name,
                email: null,
                roles: roles,
                properties: null
            );

            return helper.CreateToken(identity, TimeSpan.FromHours(1));
        }

        [Route("/account")]
        [JwtAuth]
        public sealed class JwtModuleAccountController : Controller {

            [AllowAnonymous]
            [Route("GET", "/public")]
            public object Public() {
                return new {
                    ok = true,
                    isAuthenticated = Principal.IsAuthenticated
                };
            }

            [Route("GET", "/me")]
            public object Me() {
                return new {
                    isAuthenticated = Principal.IsAuthenticated,
                    id = Principal.Identity.Identifier,
                    name = Principal.Name,
                    authenticationType = Principal.Identity.AuthenticationType
                };
            }

            [RequireRole("admin")]
            [Route("GET", "/admin")]
            public object Admin() {
                return new {
                    ok = true,
                    area = "admin"
                };
            }

        }

        [Route("/custom")]
        [JwtAuth]
        public sealed class JwtModuleCustomPrincipalController : Controller {

            [Route("GET", "/me")]
            public object Me() {
                return new {
                    authenticationType = Principal.Identity.AuthenticationType,
                    loginUrl = Principal.Get("login_url"),
                    returnUrlParameter = Principal.Get("return_url_parameter"),
                    source = Principal.Get("source"),
                    tokenSubject = Principal.Get("token_subject")
                };
            }

        }

    }

}
