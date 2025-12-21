using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            await server.RunAsync();
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {

        [Route("GET", "/test1a")]
        public object Test1a() {
            // status code 200
            // the contentType is default to "application/json"
            // body : the object will be serialized to json string
            // the compression is disabled by default
            return Response.MakeResponse(new { message = "Hello World !" });
        }

        [Route("GET", "/test1b")]
        public object Test1b() {
            // status code 200
            // change the contentType to "text/plain"
            // body : set the string
            // set the decompression to "gzip"
            return Response.MakeResponse("Hello World !", contentType: "text/plain", compress: new string[] { "gzip" });
        }

        [Route("GET", "/test1c")]
        public object Test1c() {
            // status code 200
            // the contentType is default to "application/json"
            // body : the object will be serialized to json string
            // the compression is disabled by default
            // add a new header "Date" which contains the current datetime
            return Response.MakeResponse(new { message = "Hello, World !" }, addHeaders: new Dictionary<string, string>() { { "Date", DateTime.UtcNow.ToString("o") } });
        }

        [Route("GET", "/test2")]
        public object Test2() {
            try {
                throw new Exception("test2");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 500
                return Response.MakeInternalServerErrorResponse(ex.Message);
            }
        }

        [Route("GET", "/test3")]
        public object Test3() {
            try {
                throw new KeyNotFoundException("test3");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 404
                return Response.MakeNotFoundResponse(ex.Message);
            }
        }

        [Route("GET", "/test4")]
        public object Test4() {
            try {
                throw new UnauthorizedAccessException("test4");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 401
                return Response.MakeUnAuthorizedResponse(ex.Message);
            }
        }

        [Route("GET", "/test5")]
        public object Test5() {
            var content = "download text content";
            // will force download a file "file.txt" with content
            return MakeDownloadResponse(content, "file.txt");
        }

    }

}