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

        [Theory]
        [InlineData(AuthorizeResult.Forbidden)]
        [InlineData(AuthorizeResult.Challenge)]
        [InlineData((AuthorizeResult)99)]
        public async Task ServerSentEvents_Should_Return_Forbidden_When_Authorize_Denies(AuthorizeResult decision) {

            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseServerSentEventsModule(options => {
                options.Authorize = _ => decision;
            });

            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = new HttpClient();
                using var response = await client.GetAsync($"http://{server.Address}:{server.Port}/sse");

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
        public async Task ServerSentEvents_Should_Challenge_Only_When_Requested(AuthorizeResult decision, HttpStatusCode expectedStatus, int expectedChallenges) {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            int challengeCount = 0;

            server.ConfigureChallenge(session => {
                challengeCount++;
                return session.Response.Status(401).Text("Authenticate").SendAsync();
            });

            server.UseServerSentEventsModule(options => {
                options.Authorize = _ => decision;
            });

            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = new HttpClient();
                using var response = await client.GetAsync($"http://{server.Address}:{server.Port}/sse");

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
