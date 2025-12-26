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
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Text("Hello World !");
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/plain");
            Check.That(content).IsEqualTo("Hello World !");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_HelloWorld_Json() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Json(new { message = "Hello World !" });
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_HelloWorld_Html() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Html("<h1>Hello World !</h1>");
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
            Check.That(content).IsEqualTo("<h1>Hello World !</h1>");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_RemoveBody() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Html("<h1>Hello World !</h1>").RemoveBody();
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
            Check.That(content).IsEmpty();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_404_NotFound() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.NotFound("NotFound");
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            Check.That(content).IsEqualTo("NotFound");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_500_ServerError() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.InternalServerError("Server Error");
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            Check.That(content).IsEqualTo("Server Error");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_401_Unauthorized() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Unauthorized("Unauthorized");
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            Check.That(content).IsEqualTo("Unauthorized");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_302_Redirect() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Redirect($"http://{server.Address}:{server.Port}/");
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_401_Access() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Access();
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_403_Access() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                //session.webuser = new WebUser() { Identity = true };
                return session.Response.Access(true);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

    }

}
