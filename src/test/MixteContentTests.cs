using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.Modules;
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

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/";
                options.AutoIndex = true;
            });

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MixteContent_Index() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/";
                options.AutoIndex = true;
            });

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).Contains("Index of /");
            Check.That(content).Contains("SimpleW.dll");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }


    }

}
