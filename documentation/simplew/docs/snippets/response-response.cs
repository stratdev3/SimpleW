using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // minimal api
            server.MapGet("/", (HttpSession session) => {
                return session.Response
                              .Html("""
                                    <p>Index</p>
                                    <ul>
                                        <li><a href="/api/json">Json</li>
                                        <li><a href="/api/plaintext">Text</li>
                                        <li><a href="/api/css">CSS</li>
                                    </ul>
                               """);
            });

            // controllers
            server.MapControllers<Controller>("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    public class TestController : Controller {

        [Route("GET", "/json")]
        public object Json() {
            return Response.Json("<p>Hello, World</p>")
                           .AddHeader("X-trace-id", "1234") // add custom header
                           .AddHeader("X-custom", "test");
        }

        [Route("GET", "/plaintext")]
        public object Plaintext() {
            return Response.Text("Hello World !") // set body test
                           .SetCookie("mycookie", "myvalue", new HttpResponse.CookieOptions(secure: true, maxAgeSeconds: 900)); // add cookie
        }

        [Route("GET", "/css")]
        public object Css() {
            return Response.Text(".items { background: #fc3; }") // set body text
                           .ContextTypeFromExtension(".css"); // change content type
        }

        [Route("GET", "/download")]
        public object DownloadArchive() {
            return Response.File(@"C:\www\archive.zip"); // send file download
        }

    }

}