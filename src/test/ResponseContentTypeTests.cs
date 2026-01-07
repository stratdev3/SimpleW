using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for response content type
    /// </summary>
    public class ResponseContentTypeTests {

        [Fact]
        public async Task Response_ContentType_Text() {

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
        public async Task Response_ContentType_Json() {

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
        public async Task Response_ContentType_Html() {

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
        public async Task Response_WitoutBody() {

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

    }

}
