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

        [Theory]
        [InlineData(AuthorizeResult.Forbidden)]
        [InlineData(AuthorizeResult.Challenge)]
        [InlineData((AuthorizeResult)99)]
        public async Task WebSocket_Should_Return_Forbidden_When_Authorize_Denies(AuthorizeResult decision) {

            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseWebSocketModule(options => {
                options.Authorize = _ => decision;
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

        [Theory]
        [InlineData(AuthorizeResult.Challenge, HttpStatusCode.Unauthorized, 1)]
        [InlineData(AuthorizeResult.Forbidden, HttpStatusCode.Forbidden, 0)]
        [InlineData((AuthorizeResult)99, HttpStatusCode.Forbidden, 0)]
        public async Task WebSocket_Should_Challenge_Only_When_Requested(AuthorizeResult decision, HttpStatusCode expectedStatus, int expectedChallenges) {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            int challengeCount = 0;

            server.ConfigureChallenge(session => {
                challengeCount++;
                return session.Response.Status(401).Text("Authenticate").SendAsync();
            });

            server.UseWebSocketModule(options => {
                options.Authorize = _ => decision;
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

                Check.That(response.StatusCode).Is(expectedStatus);
                Check.That(await response.Content.ReadAsStringAsync()).IsEqualTo(expectedChallenges == 1 ? "Authenticate" : "Forbidden");
                Check.That(challengeCount).IsEqualTo(expectedChallenges);
            }
            finally {
                if (started) {
                    await server.StopAsync();
                }
            }
        }

    }

}
