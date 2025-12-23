using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.UseControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/forge")]
        public object Forge() {
            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "John Doe" },
                { "roles", new string[] { "account", "infos" } }
            };
            // return the json web token string
            // with payload
            // crypt by "secret" passphrase (algo: HS256)
            // and expired in 15 minutes
            return NetCoreServerExtension.CreateJwt(payload, "secret", expiration: 15*60);
        }

    }

    public class UserToken {
        public Guid id { get; set; }
        public string name { get; set; }
        public string[] roles { get; set; } = new string[0];
    }

}