using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for response
    /// </summary>
    public class ResponseTests {

        [Fact]
        public async Task Response_200_HelloWorld_Raw() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return session.Response.MakeGetResponse("Hello World !");
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("Hello World !");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_HelloWorld_Json() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_404_NotFound() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session) => {
                return Session.Response.MakeNotFoundResponse("NotFound");
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            Check.That(content).IsEqualTo("NotFound");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_500_ServerError() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session) => {
                return Session.Response.MakeInternalServerErrorResponse("Server Error");
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            Check.That(content).IsEqualTo("Server Error");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_401_Unauthorized() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session) => {
                return Session.Response.MakeUnAuthorizedResponse("Unauthorized");
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            Check.That(content).IsEqualTo("Unauthorized");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_302_Redirect() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session) => {
                return Session.Response.MakeRedirectResponse($"http://{server.Address}:{server.Port}/");
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_401_Access() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session) => {
                return Session.Response.MakeAccessResponse();
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_403_Access() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session) => {
                Session.webuser = new WebUser() { Identity = true };
                return Session.Response.MakeAccessResponse();
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

    }

}
