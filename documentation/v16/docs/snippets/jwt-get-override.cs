using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    class BaseController : Controller {

        // override GetJwt()
        protected override string GetJwt() {
            // 1. the jwt is extract from the "token" query string
            var route = new Route(Request);
            var qs = Route.ParseQueryString(route?.Url?.Query);
            var token = qs["token"]?.ToString();
            if (!string.IsNullOrWhiteSpace(token)) {
                return token;
            }

            // 2. the jwt is extract from "business-rule" http header
            return Request.Header("business-rule");
        }
    }

    [Route("/test")]
    class TestController : BaseController {

        [Route("GET", "/token")]
        public object Token() {
            return this.GetJwt();
        }

    }

}