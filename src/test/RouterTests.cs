using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Router
    /// </summary>
    public class RouterTests {

        #region map

        #region method get

        [Fact]
        public async Task Router_MapGet_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Router_MapGet_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/does-not-exists");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion method get

        #region method post

        [Fact]
        public async Task Router_MapPost_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapPost("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/test/hello", new StringContent("payload"));

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Router_MapPost_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapPost("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/does-not-exists", new StringContent("payload"));

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion method post

        #region method head

        [Fact]
        public async Task Router_MapHead_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.Map("HEAD", "/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Head, $"http://{server.Address}:{server.Port}/api/test/hello");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Router_MapHead_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.Map("HEAD", "/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Head, $"http://{server.Address}:{server.Port}/api/does-not-exists");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion method head

        #region route with wildcard

        [Fact]
        public async Task Router_MapGet_Wildcard_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/api/test/*", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Router_Wildcard_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/api/test/*", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/does-not-exists");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion route with wildcard

        #region route parameter name

        [Fact]
        public async Task Router_MapGet_RouteParameterName_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/api/test/hello/:name", (string name) => {
                return new { message = $"{name}, Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello/Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Router_MapGet_RouteParameterName_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/api/test/hello/:name", (string name) => {
                return new { message = $"{name}, Hello World !" };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion route parameter name

        #endregion map

        #region mapcontroller

        #region method get

        [Fact]
        public async Task Router_MapControllerGet_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerGet_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerGet_200_Controller : Controller {
            [Route("GET", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        [Fact]
        public async Task Router_MapControllerGet_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerGet_404_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/does-not-exists");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerGet_404_Controller : Controller {
            [Route("GET", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        #endregion method get

        #region method post

        [Fact]
        public async Task Router_MapControllerPost_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerPost_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/test/hello", new StringContent("payload"));

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerPost_200_Controller : Controller {
            [Route("POST", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        [Fact]
        public async Task Router_MapControllerPost_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerPost_404_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/does-not-exists", new StringContent("payload"));

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerPost_404_Controller : Controller {
            [Route("POST", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        #endregion method post

        #region method head

        [Fact]
        public async Task Router_MapControllerHead_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerHead_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Head, $"http://{server.Address}:{server.Port}/api/test/hello");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerHead_200_Controller : Controller {
            [Route("HEAD", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        [Fact]
        public async Task Router_MapControllerHead_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerHead_404_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Head, $"http://{server.Address}:{server.Port}/api/does-not-exists");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerHead_404_Controller : Controller {
            [Route("HEAD", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        #endregion method head

        #region route on method only

        [Fact]
        public async Task Router_MapControllerGet_MethodOnly_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerGet_MethodOnly_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        public class Router_MapControllerGet_MethodOnly_200_Controller : Controller {
            [Route("GET", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        [Fact]
        public async Task Router_MapControllerGet_MethodOnly_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerGet_MethodOnly_404_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/does-not-exists");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        public class Router_MapControllerGet_MethodOnly_404_Controller : Controller {
            [Route("GET", "/hello")]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        #endregion route on method only

        #region absolute url on route method

        [Fact]
        public async Task Router_MapControllerGet_AbsoluteUrl_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerGet_AbsoluteUrl_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/absolute/url/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerGet_AbsoluteUrl_200_Controller : Controller {
            [Route("GET", "/absolute/url/hello", isAbsolutePath: true)]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        [Fact]
        public async Task Router_MapControllerGet_AbsoluteUrl_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapControllerGet_AbsoluteUrl_404_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/does-not-exists");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapControllerGet_AbsoluteUrl_404_Controller : Controller {
            [Route("GET", "/absolute/url/hello", isAbsolutePath: true)]
            public object Hello() {
                return new { message = "Hello World !" };
            }
        }

        #endregion absolute url on route method

        #region route with wildcard

        [Fact]
        public async Task Route_MapController_Wildcard_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapController<Route_MapController_Wildcard_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/helloworld");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        public class Route_MapController_Wildcard_200_Controller : Controller {
            [Route("GET", "/*")]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        [Fact]
        public async Task Route_MapController_Wildcard_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapController<Route_MapController_Wildcard_404_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/does-not-exist/helloworld");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        public class Route_MapController_Wildcard_404_Controller : Controller {
            [Route("GET", "/*")]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        #endregion route with wildcard

        #region route with wildcard absolute

        [Fact]
        public async Task Route_MapController_Wildcard_Absolute_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapController<Route_MapController_Wildcard_Absolute_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/will-exist");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Route_MapController_Wildcard_Absolute_200_Controller : Controller {
            [Route("GET", "/*", isAbsolutePath: true)]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        #endregion route with absolute

        #region route parameter name

        [Fact]
        public async Task Router_MapController_RoutePamaterName_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapController_RoutePamaterName_200_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello/Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapController_RoutePamaterName_200_Controller : Controller {
            [Route("GET", "/hello/:name")]
            public object Hello(string name) {
                return new { message = $"{name}, Hello World !" };
            }
        }

        [Fact]
        public async Task Router_MapController_RoutePamaterName_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Router_MapController_RoutePamaterName_404_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        [Route("/test")]
        public class Router_MapController_RoutePamaterName_404_Controller : Controller {
            [Route("GET", "/hello/:name")]
            public object Hello(string name) {
                return new { message = $"{name}, Hello World !" };
            }
        }

        #endregion route parameter name

        #endregion mapcontroller

        #region mapcontrollers subclass

        [Fact]
        public async Task Router_MapControllerSubclass_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapControllers<Subclass>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        public class Subclass : Controller {

        }

        [Route("/test")]
        public class Router_MapControllerSubclass_200_Controller : Subclass {

            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = $"Hello World !" };
            }

        }

        #endregion mapcontrollers subclass

    }

}
