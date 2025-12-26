using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // set basic auth module
            server.UseBasicAuthModule(o => {
                o.Prefix = "/api/test/hello";
                o.Realm = "Admin";
                o.Users = new[] { new BasicAuthModuleExtension.BasicAuthOptions.BasicUser("chef", "pwd") };
            })
            // set another basic auth module
            .UseBasicAuthModule(o => {
                o.Prefix = "/metrics";
                o.Realm = "Metrics";
                o.CredentialValidator = (u, p) => u == "prom" && p == "scrape";
            });

            server.MapControllers<Controller>("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    public class SomeController : Controller {

        [Route("GET", "/test")]
        public object SomePublicMethod() {
            return new {
                message = "Hello World !"
            };
        }

    }

}