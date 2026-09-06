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

            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
                Check.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Forbidden");
            }
            finally {
                if (started) {
                    await server.StopAsync();
                }
            }
        }

        [Fact]
        public async Task ServerSentEvents_Should_Use_Server_Challenge_When_Authorize_Denies() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            int challengeCount = 0;

            server.ConfigureChallenge(session => {
                challengeCount++;
                return session.Response.Status(401).Text("Authenticate").SendAsync();
            });

            server.UseServerSentEventsModule(options => {
                options.Authorize = _ => false;
            });

            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = new HttpClient();
                using var response = await client.GetAsync($"http://{server.Address}:{server.Port}/sse");

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
