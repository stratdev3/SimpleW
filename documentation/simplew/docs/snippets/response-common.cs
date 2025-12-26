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

        [Route("GET", "/test1")]
        public object Test1() {
            try {
                throw new KeyNotFoundException("test3");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 404
                return Response.NotFound(ex.Message);
            }
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
                return Response.InternalServerError(ex.Message);
            }
        }

        [Route("GET", "/test3")]
        public object Test3() {
            try {
                throw new UnauthorizedAccessException("test4");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 401
                return Response.Unauthorized(ex.Message);
            }
        }

        [Route("GET", "/test4")]
        public object Test5() {
            return Response.Redirect("https://stratdev3.github.io/SimpleW/");
        }

    }

}