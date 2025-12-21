using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directories/endpoints
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\frontend\";
                options.Prefix = "/";
                options.CacheTimeout = TimeSpan.FromDays(1);
            });
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\public\";
                options.Prefix = "/public/";
                options.CacheTimeout = TimeSpan.FromDays(1);
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            await server.RunAsync();
        }
    }
}