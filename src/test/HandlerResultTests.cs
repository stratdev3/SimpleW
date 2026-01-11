using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for HandlerResult
    /// </summary>
    public class HandlerResultTests {

        [Fact]
        public async Task Response_200_Default_HandlerResult() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Custom_HandlerResult_AddHeader() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.ConfigureHandlerResult(async (session, result) => {
                await session.Response
                             .AddHeader("custom", "value")
                             .Html($"<p>{result.ToString()}</p>")
                             .SendAsync();
            });
            server.MapGet("/", () => {
                return "Hello World !";
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/html");
            Check.That(content).IsEqualTo("<p>Hello World !</p>");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

    }

}
