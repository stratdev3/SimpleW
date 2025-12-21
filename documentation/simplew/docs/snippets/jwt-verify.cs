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

    [Route("/user")]
    public class TestController : Controller {

        [Route("GET", "/account")]
        public object Account() {
            var jwt = this.GetJwt();

            // ValidateJwt
            // Success : return an instance of T class and map jwt payload to all public properties
            // Invalid/Error : return null
            var userToken = jwt?.ValidateJwt<UserToken>("secret");

            if (userToken == null || !userToken.roles.Contains("account")) {
                return MakeUnAuthorizedResponse("private access, need account.");
            }

            return $"you have access to your account {userToken.id}";
        }

    }

    public class UserToken {
        public Guid id { get; set; }
        public string name { get; set; }
        public string[] roles { get; set; } = new string[0];
    }

}