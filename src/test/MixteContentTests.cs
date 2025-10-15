using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for mixte content
    /// </summary>
    public class MixteContentTests {

        [Fact]
        public async Task MixteContent_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            server.AutoIndex = true;

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MixteContent_Index() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            server.AutoIndex = true;

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).Contains("Index of /");
            Check.That(content).Contains("SimpleW.dll");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }


    }

}
