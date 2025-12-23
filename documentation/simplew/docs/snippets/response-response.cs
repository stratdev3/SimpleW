using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    public class TestController : Controller {

        [Route("GET", "/html")]
        public object Html() {
            return MakeCustomResponse(200, "<p>Hello, World</p>", "text/html");
        }

        [Route("GET", "/json")]
        public object Json() {
            return MakeCustomResponse(200, """{ "message": "Hello World !" }""", "application/json");
        }

        [Route("GET", "/plaintext")]
        public object Plaintext() {
            return MakeCustomResponse(200, "Hello World !", "text/plain");
        }

        /// <summary>
        /// My Custom Response
        /// </summary>
        /// <param name="code">the http code</param>
        /// <param name="content">the content</param>
        /// <param name="contentType">the Content-Type header value</param>
        /// <returns></returns>
        private object MakeCustomResponse(int code, string content, string contentType) {
            Response.Clear();
            Response.SetBegin(code);
            Response.SetHeader("Content-Type", contentType);
            Response.SetBody(content);
            return Response;
        }

    }

}