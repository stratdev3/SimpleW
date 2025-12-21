using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // use the StaticFilesModule
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\spa\";                  // serve your files located here
                options.Prefix = "/";                           // to "/" endpoint
                options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
                options.AutoIndex = true;                       // enable autoindex if no index.html exists in the directory
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }
}