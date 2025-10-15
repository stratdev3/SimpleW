using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for CORS
    /// </summary>
    public class CorsTests {

        [Fact]
        public async Task Cors_DynamicContent_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddCORS(
                "*",                // Access-Control-Allow-Origin
                "*, Authorization", // Access-Control-Allow-Headers
                "GET,POST",         // Access-Control-Allow-Methods
                "true"              // Access-Control-Allow-Credentials
            );

            server.AddDynamicContent(typeof(Cors_DynamicContent_HelloWorld_Controller), "/");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Origin").First()).IsEqualTo("*");
            Check.That(response.Headers.Contains("Access-Control-Allow-Headers")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Headers").First()).IsEqualTo("*, Authorization");
            Check.That(response.Headers.Contains("Access-Control-Allow-Methods")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Methods").First()).IsEqualTo("GET,POST");
            Check.That(response.Headers.Contains("Access-Control-Allow-Credentials")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Credentials").First()).IsEqualTo("true");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        public class Cors_DynamicContent_HelloWorld_Controller : Controller {
            [Route("GET", "/", isAbsolutePath: true)]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }


        [Fact]
        public async Task Cors_InlineFuncContent_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddCORS(
                "*",                // Access-Control-Allow-Origin
                "*, Authorization", // Access-Control-Allow-Headers
                "GET,POST",         // Access-Control-Allow-Methods
                "true"              // Access-Control-Allow-Credentials
            );

            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Origin").First()).IsEqualTo("*");
            Check.That(response.Headers.Contains("Access-Control-Allow-Headers")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Headers").First()).IsEqualTo("*, Authorization");
            Check.That(response.Headers.Contains("Access-Control-Allow-Methods")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Methods").First()).IsEqualTo("GET,POST");
            Check.That(response.Headers.Contains("Access-Control-Allow-Credentials")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Credentials").First()).IsEqualTo("true");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }


        [Fact]
        public async Task Cors_StaticContent_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddCORS(
                "*",                // Access-Control-Allow-Origin
                "*, Authorization", // Access-Control-Allow-Headers
                "GET,POST",         // Access-Control-Allow-Methods
                "true"              // Access-Control-Allow-Credentials
            );

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), timeout: TimeSpan.FromDays(1));
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Origin").First()).IsEqualTo("*");
            Check.That(response.Headers.Contains("Access-Control-Allow-Headers")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Headers").First()).IsEqualTo("*, Authorization");
            Check.That(response.Headers.Contains("Access-Control-Allow-Methods")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Methods").First()).IsEqualTo("GET,POST");
            Check.That(response.Headers.Contains("Access-Control-Allow-Credentials")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Credentials").First()).IsEqualTo("true");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Cors_StaticContentNoCache_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddCORS(
                "*",                // Access-Control-Allow-Origin
                "*, Authorization", // Access-Control-Allow-Headers
                "GET,POST",         // Access-Control-Allow-Methods
                "true"              // Access-Control-Allow-Credentials
            );

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(response.Headers.Contains("Access-Control-Allow-Origin")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Origin").First()).IsEqualTo("*");
            Check.That(response.Headers.Contains("Access-Control-Allow-Headers")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Headers").First()).IsEqualTo("*, Authorization");
            Check.That(response.Headers.Contains("Access-Control-Allow-Methods")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Methods").First()).IsEqualTo("GET,POST");
            Check.That(response.Headers.Contains("Access-Control-Allow-Credentials")).IsTrue();
            Check.That(response.Headers.GetValues("Access-Control-Allow-Credentials").First()).IsEqualTo("true");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

    }

}
