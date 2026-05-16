using System.Net;
using System.Net.Http;
using NFluent;
using SimpleW;
using SimpleW.Modules;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for WebSocket content
    /// </summary>
    public class WebSocketModuleTests {

        [Fact]
        public async Task WebSocket_Should_Return_Forbidden_When_Authorize_Denies() {

            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.UseWebSocketModule(options => {
                options.Authorize = _ => false;
            });

            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = new HttpClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, $"http://{server.Address}:{server.Port}/ws");
                request.Version = HttpVersion.Version11;
                request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
                request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
                request.Headers.TryAddWithoutValidation("Upgrade", "websocket");
                request.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==");
                request.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");

                using var response = await client.SendAsync(request);

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
