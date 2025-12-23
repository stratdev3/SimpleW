using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // minimal api
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });
            server.MapGet("/api/test", () => {
                return new { message = "authenticated" };
            });

            // use middleware as firewall/authenticate
            server.UseMiddleware(static (session, next) => {
                // check if the user is authorized ?
                if (session.Request.Path.StartsWith("/api", StringComparison.Ordinal)) {
                    if (!session.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != "secret") {
                        // stop the pipeline here by sending a 401
                        return session.Unauthorized("You're authorized in this area");
                    }
                }
                // continue the pipeline
                return next();
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }

}