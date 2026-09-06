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

            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
                Check.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Forbidden");
            }
            finally {
                if (started) {
                    await server.StopAsync();
                }
            }
        }

        [Fact]
        public async Task WebSocket_Should_Use_Server_Challenge_When_Authorize_Denies() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            int challengeCount = 0;

            server.ConfigureChallenge(session => {
                challengeCount++;
                return session.Response.Status(401).Text("Authenticate").SendAsync();
            });

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

                Check.That(response.StatusCode).Is(HttpStatusCode.Unauthorized);
                Check.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Authenticate");
                Check.That(challengeCount).IsEqualTo(1);
            }
            finally {
                if (started) {
                    await server.StopAsync();
                }
            }
        }

    }

}
