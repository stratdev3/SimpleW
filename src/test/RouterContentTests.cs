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

        #region dynamicmethod

        [Fact]
        public async Task Route_DynamicMethod_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddDynamicContent(typeof(Route_DynamicMethod_200_Controller), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        public class Route_DynamicMethod_200_Controller : Controller {
            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        #endregion dynamicmethod

        #region dynamicmethodcontroller

        [Fact]
        public async Task Route_DynamicControllerMethod_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddDynamicContent(typeof(Route_DynamicControllerMethod_200_Controller), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Route_DynamicControllerMethod_200_Controller : Controller {
            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        #endregion dynamicmethodcontroller

        #region dynamicmethodcontrollerabsolute

        [Fact]
        public async Task Route_DynamicControllerMethodAbsolute_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddDynamicContent(typeof(Route_DynamicControllerMethodAbsolute_Controller), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Route_DynamicControllerMethodAbsolute_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddDynamicContent(typeof(Route_DynamicControllerMethodAbsolute_Controller), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Route_DynamicControllerMethodAbsolute_Controller : Controller {
            [Route("GET", "/hello", isAbsolutePath: true)]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        #endregion dynamicmethodcontrollerabsolute

        #region dynamic_catchall

        [Fact]
        public async Task Route_Dynamic_CatchAll_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // need by MaintenanceController wildcard route parameter
            server.Router.RegExpEnabled = true;

            server.AddDynamicContent(typeof(Route_Dynamic_CatchAll_Controller), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/helloworld");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Route_Dynamic_CatchAll_RegexpFalse_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // need by MaintenanceController wildcard route parameter
            server.Router.RegExpEnabled = false;

            server.AddDynamicContent(typeof(Route_Dynamic_CatchAll_Controller), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/helloworld");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        public class Route_Dynamic_CatchAll_Controller : Controller {
            [Route("GET", "/*")]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        #endregion dynamic_catchall

        #region onbeforemethod

        [Fact]
        public async Task OnBeforeController_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddDynamicContent(typeof(OnBeforeController), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            server.Stop();
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

        #endregion onbeforemethod

        #region subclass

        [Fact]
        public async Task Subclass_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddDynamicContent(typeof(SubclassController), "/api");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Subclass : Controller {

            protected string Name = "Chris";

        }

        [Route("/test")]
        public class SubclassController : Subclass {

            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = $"{Name}, Hello World !" };
            }

        }

        #endregion subclass

    }

}
