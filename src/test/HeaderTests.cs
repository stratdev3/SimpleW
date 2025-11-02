using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for headers
    /// </summary>
    public class HeaderTests {

        [Fact]
        public async Task Response_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
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
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
            Check.That(response.Content.Headers.ContentType?.CharSet).IsEqualTo("UTF-8");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_ContentType() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return session.Response.MakeResponse("Hello World !", contentType: "plain/text");
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("Hello World !");
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("plain/text");
            Check.That(response.Content.Headers.ContentType?.CharSet).IsNull();

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_CustomHeader() {

            // settings
            var now = DateTime.Now;

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return session.Response.MakeResponse(new { message = "Hello World !" }, addHeaders: new Dictionary<string, string> { { "Date", now.ToString("o") } });
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
            Check.That(response.Content.Headers.ContentType?.CharSet).IsEqualTo("UTF-8");
            Check.That(response.Headers.Contains("Date")).IsTrue();
            Check.That(response.Headers.GetValues("Date").First()).IsEqualTo(now.ToString("o"));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

    }

}
