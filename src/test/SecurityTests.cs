using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;
using static test.SecurityTests.Dynamic_TokenController;


namespace test {

    /// <summary>
    /// Tests for Security
    /// </summary>
    public class SecurityTests {

        public const string secret = "minimumlongsecret";

        #region trustXHeaders

        [Fact]
        public async Task Security_host() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // TrustXHeaders is default false

            server.MapGet("/", (NetCoreServer.HttpRequest Request) => {
                return new { Request.FullQualifiedUrl };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            await Task.Delay(50); // give a little time to logger

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { FullQualifiedUrl = $"http://{server.Address}:{server.Port}/" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Security_TrustXHeaders_False() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // TrustXHeaders is default false

            server.MapGet("/", (NetCoreServer.HttpRequest Request) => {
                return new { Request.FullQualifiedUrl };
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-Host", "fakehost");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            await Task.Delay(50); // give a little time to logger

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { FullQualifiedUrl = $"http://{server.Address}:{server.Port}/" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Security_TrustXHeaders_True() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // eanble TrustXHeaders
            server.TrustXHeaders = true;

            server.MapGet("/", (NetCoreServer.HttpRequest Request) => {
                return new { Request.FullQualifiedUrl };
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-Host", "trusthost");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            await Task.Delay(50); // give a little time to logger

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { FullQualifiedUrl = $"http://trusthost/" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        #endregion trustXHeaders

        #region MapGet_jwt

        [Fact]
        public async Task MapGet_TokenForge_Qs() {

            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "Chris" },
                { "roles", new string[] { "admin" } }
            };
            string jwt = NetCoreServerExtension.CreateJwt(payload, secret, expiration: 15 * 60);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                NetCoreServerExtension.ParseQueryString(Request.Url, "jwt", out string qs_jwt);
                return new { jwt = qs_jwt };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?jwt={jwt}");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { jwt }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_TokenForge_Header() {

            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "Chris" },
                { "roles", new string[] { "admin" } }
            };
            string jwt = NetCoreServerExtension.CreateJwt(payload, secret, expiration: 15 * 60);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                return new { jwt = Request.HeaderAuthorization["Bearer ".Length..] };
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { jwt }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_TokenVerify() {

            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "Chris" },
                { "roles", new string[] { "admin" } }
            };
            string jwt = NetCoreServerExtension.CreateJwt(payload, secret, expiration: 15 * 60);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                string token = Request.HeaderAuthorization["Bearer ".Length..];
                var userToken = token?.ValidateJwt<UserToken>(secret);
                return userToken;
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new UserToken() { id = Guid.Parse(payload["id"].ToString() ?? ""), name = payload["name"].ToString() ?? "", roles = (string[])payload["roles"] }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_Webuser() {

            var webuser = new WebUser() {
                Identity = true,
                Id = Guid.NewGuid(),
                FullName = "Chris",
                Login = "chris",
                Mail = "chris@stratdev.fr",
                Profile = "Administrator",
                Roles = new string[] { "admin" }
            };
            string jwt = NetCoreServerExtension.CreateJwt(webuser, secret, "localhost", 15 * 60, refresh: false);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.SetToken(secret, "localhost");
            server.MapGet("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                string token = Request.HeaderAuthorization["Bearer ".Length..];
                var webuser = token?.ValidateJwt<WebUser>(secret);
                return webuser;
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<WebUser>(content))).IsEqualTo(JsonSerializer.Serialize(webuser));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        #endregion MapGet_jwt

        #region Dynamic_jwt

        [Fact]
        public async Task Dynamic_TokenForge_Qs() {

            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "Chris" },
                { "roles", new string[] { "admin" } }
            };
            string jwt = NetCoreServerExtension.CreateJwt(payload, secret, expiration: 15 * 60);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.AddDynamicContent(typeof(Dynamic_TokenController), "/api");
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/forge?jwt={jwt}");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { jwt }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Dynamic_TokenForge_Header() {

            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "Chris" },
                { "roles", new string[] { "admin" } }
            };
            string jwt = NetCoreServerExtension.CreateJwt(payload, secret, expiration: 15 * 60);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.AddDynamicContent(typeof(Dynamic_TokenController), "/api");
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/forge");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { jwt }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Dynamic_TokenVerify() {

            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "Chris" },
                { "roles", new string[] { "admin" } }
            };
            string jwt = NetCoreServerExtension.CreateJwt(payload, secret, expiration: 15 * 60);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.AddDynamicContent(typeof(Dynamic_TokenController), "/api");
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/account");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new UserToken() { id = Guid.Parse(payload["id"].ToString() ?? ""), name = payload["name"].ToString() ?? "", roles = (string[])payload["roles"] }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Dynamic_Webuser() {

            var webuser = new WebUser() {
                Identity = true,
                Id = Guid.NewGuid(),
                FullName = "Chris",
                Login = "chris",
                Mail = "chris@stratdev.fr",
                Profile = "Administrator",
                Roles = new string[] { "admin" }
            };
            string jwt = NetCoreServerExtension.CreateJwt(webuser, secret, "localhost", 15 * 60, refresh: false);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.SetToken(secret, "localhost");
            server.AddDynamicContent(typeof(Dynamic_TokenController), "/api");
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/user");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<WebUser>(content))).IsEqualTo(JsonSerializer.Serialize(webuser));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Dynamic_TokenController : Controller {

            [Route("GET", "/forge")]
            public object Forge() {
                return new { jwt = GetJwt() };
            }

            [Route("GET", "/account")]
            public object? Account() {
                var jwt = this.GetJwt();
                var userToken = jwt?.ValidateJwt<UserToken>(secret);
                return userToken;
            }

            [Route("GET", "/user")]
            public object User() {
                return webuser;
            }

            public class UserToken {
                public Guid id { get; set; }
                public string name { get; set; } = "";
                public string[] roles { get; set; } = new string[0];
            }

        }

        #endregion Dynamic_jwt

    }

}
