using System.Net;
using NFluent;
using SimpleW;
using SimpleW.Modules;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Server-Sent Events content
    /// </summary>
    public class ServerSentEventsModuleTests {

        [Fact]
        public async Task ServerSentEvents_Should_Return_Forbidden_When_Authorize_Denies() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseServerSentEventsModule(options => {
                options.Authorize = _ => false;
            });

            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = new HttpClient();
                using var response = await client.GetAsync($"http://{server.Address}:{server.Port}/sse");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                if (started) {
                    await server.StopAsync();
                }
                PortManager.ReleasePort(port);
            }
        }

    }

}
