using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // minimal api
            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            // use middleware for logging
            server.UseMiddleware(static async (session, next) => {
                // start a timer
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    await next(); // continue the pipeline (and so send the response)
                }
                finally {
                    // back from pipeline (the response has been sent)
                    sw.Stop();
                    Console.WriteLine($"[{DateTime.UtcNow:O}] {session.Request.Method} {session.Request.Path} in {sw.ElapsedMilliseconds} ms");
                }
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }

}