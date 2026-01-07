using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for RouterExecutor
    /// </summary>
    public class RouterExecutorTests {

        [Fact]
        public async Task OnBeforeController_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapController<OnBeforeController>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class OnBeforeController : Controller {

            private string Name = "";

            public override void OnBeforeMethod() {
                Name = "Chris";
            }

            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = $"{Name}, Hello World !" };
            }

        }

    }

}
