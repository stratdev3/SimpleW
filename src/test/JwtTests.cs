using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NFluent;
using SimpleW;
using SimpleW.Security;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Jwt
    /// </summary>
    public class JwtTests {

        public const string secret = "minimumlongsecret";

        [Fact]
        public async Task MapGet_TokenForge_Qs() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // payload
            var webuser = new WebUser() {
                Identity = true,
                Id = Guid.NewGuid(),
                FullName = "Chris",
                Login = "chris",
                Mail = "chris@stratdev.fr",
                Profile = "Administrator",
                Roles = ["admin"]
            };
            string jwt = Jwt.EncodeHs256(server.JsonEngine, JwtTokenPayload.Create(TimeSpan.FromMinutes(15)), webuser.ToDict(), secret);

            server.MapGet("/", (HttpSession session) => {
                return new { jwt = session.Request.Jwt };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?jwt={jwt}");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { jwt }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_TokenForge_Header() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // payload
            var webuser = new WebUser() {
                Identity = true,
                Id = Guid.NewGuid(),
                FullName = "Chris",
                Login = "chris",
                Mail = "chris@stratdev.fr",
                Profile = "Administrator",
                Roles = ["admin"]
            };
            string jwt = Jwt.EncodeHs256(server.JsonEngine, JwtTokenPayload.Create(TimeSpan.FromMinutes(15)), webuser.ToDict(), secret);

            server.MapGet("/", (HttpSession session) => {
                return new { jwt = session.Request.Jwt };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { jwt }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_Webuser() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.Configure(options => {
                options.JwtOptions = new JwtOptions(secret) {
                    ValidateExp = false,
                    ValidateNbf = false,
                };
            });

            // payload
            var webuser = new WebUser() {
                Identity = true,
                Id = Guid.NewGuid(),
                FullName = "Chris",
                Login = "chris",
                Mail = "chris@stratdev.fr",
                Profile = "Administrator",
                Roles = ["admin"]
            };
            string jwt = Jwt.EncodeHs256(server.JsonEngine, JwtTokenPayload.Create(TimeSpan.FromMinutes(15)), webuser.ToDict(), secret);

            server.ConfigureWebUserResolver((request) => {
                if (request.JwtToken == null) {
                    return new WebUser();
                }
                try {
                    WebUser wu = request.JsonEngine.Deserialize<WebUser>(request.JwtToken.RawPayload);
                    return wu;
                }
                catch {
                    return new WebUser();
                }
            });
            server.MapGet("/", (HttpSession session) => {
                return session.Request.WebUser;
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {jwt}");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(webuser));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

    }

}
