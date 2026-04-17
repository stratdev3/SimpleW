using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.Helper.BasicAuth;
using SimpleW.Service.BasicAuth;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for BasicAuthModule / BasicAuthModuleOptions.
    /// </summary>
    public class BasicAuthModuleTests {

        [Fact]
        public async Task UseBasicAuthModule_Should_Restore_Principal_And_Honor_AllowAnonymous() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseBasicAuthModule(options => {
                options.Users = [
                    new BasicUser("admin", "secret")
                ];
            });

            server.MapController<BasicAuthModuleAccountController>("/api");

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
                meRequest.Headers.Authorization = CreateAuthorizationHeader("admin", "secret");

                HttpResponseMessage meResponse = await client.SendAsync(meRequest);
                string meContent = await meResponse.Content.ReadAsStringAsync();

                Check.That(meResponse.StatusCode).Is(HttpStatusCode.OK);
                Check.That(meContent).IsEqualTo(JsonSerializer.Serialize(new {
                    isAuthenticated = true,
                    id = "admin",
                    name = "admin",
                    authenticationType = "Basic"
                }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseBasicAuthModule_Should_Send_Challenge_From_Handler_Metadata() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseBasicAuthModule(options => {
                options.Users = [
                    new BasicUser("admin", "secret")
                ];
            });

            server.MapController<BasicAuthModuleAccountController>("/api");

            await server.StartAsync();

            try {
                using HttpClient client = new();

                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/account/ops");
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.Unauthorized);
                Check.That(content).IsEmpty();
                Check.That(response.Headers.WwwAuthenticate.Select(static h => h.ToString()).ToArray()).ContainsExactly("Basic realm=\"Ops Area\"");
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseBasicAuthModule_Should_Bypass_Options_When_Configured() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseBasicAuthModule(options => {
                options.Users = [
                    new BasicUser("admin", "secret")
                ];
                options.BypassOptionsRequests = true;
            });

            server.MapController<BasicAuthModuleAccountController>("/api");

            await server.StartAsync();

            try {
                using HttpClient client = new();
                using HttpRequestMessage request = new(HttpMethod.Options, $"http://{server.Address}:{server.Port}/api/account/preflight");

                HttpResponseMessage response = await client.SendAsync(request);
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new {
                    ok = true,
                    method = "OPTIONS"
                }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task UseBasicAuthModule_Should_Use_ModulePrincipalFactory() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseBasicAuthModule(options => {
                options.Users = [
                    new BasicUser("admin", "secret")
                ];
                options.ModulePrincipalFactory = context => new HttpPrincipal(new HttpIdentity(
                    isAuthenticated: true,
                    authenticationType: "BasicModule",
                    identifier: context.Username,
                    name: context.Username,
                    email: null,
                    roles: null,
                    properties: [
                        new IdentityProperty("realm", context.Realm),
                        new IdentityProperty("username", context.Username)
                    ]
                ));
            });

            server.MapController<BasicAuthModuleCustomPrincipalController>("/api");

            await server.StartAsync();

            try {
                using HttpClient client = new();
                using HttpRequestMessage request = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/api/custom/me");
                request.Headers.Authorization = CreateAuthorizationHeader("admin", "secret");

                HttpResponseMessage response = await client.SendAsync(request);
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new {
                    authenticationType = "BasicModule",
                    realm = "Partners Area",
                    username = "admin"
                }));
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public void UseBasicAuthModule_Should_Reject_Helper_With_Inline_Helper_Settings() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            BasicAuthHelper helper = CreateHelper();

            try {
                Assert.Throws<ArgumentException>(() => {
                    server.UseBasicAuthModule(options => {
                        options.Helper = helper;
                        options.Users = [
                            new BasicUser("admin", "secret")
                        ];
                    });
                });
            }
            finally {
                PortManager.ReleasePort(server.Port);
            }
        }

        [Fact]
        public void UseBasicAuthModule_Should_Reject_AutoAuthorize_Without_RestorePrincipal() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            try {
                Assert.Throws<ArgumentException>(() => {
                    server.UseBasicAuthModule(options => {
                        options.Users = [
                            new BasicUser("admin", "secret")
                        ];
                        options.RestorePrincipal = false;
                        options.AutoAuthorize = true;
                    });
                });
            }
            finally {
                PortManager.ReleasePort(server.Port);
            }
        }

        private static BasicAuthHelper CreateHelper() {
            return new BasicAuthHelper(options => {
                options.Users = [
                    new BasicUser("admin", "secret")
                ];
            });
        }

        private static AuthenticationHeaderValue CreateAuthorizationHeader(string username, string password) {
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            return new AuthenticationHeaderValue("Basic", payload);
        }

        [Route("/account")]
        [BasicAuth("Admin Area")]
        public sealed class BasicAuthModuleAccountController : Controller {

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

            [BasicAuth("Ops Area")]
            [Route("GET", "/ops")]
            public object Ops() {
                return new {
                    ok = true
                };
            }

            [Route("OPTIONS", "/preflight")]
            public object Preflight() {
                return new {
                    ok = true,
                    method = "OPTIONS"
                };
            }

        }

        [Route("/custom")]
        [BasicAuth("Partners Area")]
        public sealed class BasicAuthModuleCustomPrincipalController : Controller {

            [Route("GET", "/me")]
            public object Me() {
                return new {
                    authenticationType = Principal.Identity.AuthenticationType,
                    realm = Principal.Get("realm"),
                    username = Principal.Get("username")
                };
            }

        }

    }

}
