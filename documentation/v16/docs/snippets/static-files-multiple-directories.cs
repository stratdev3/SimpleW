using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directories/endpoints
            server.AddStaticContent(@"C:\www\frontend", "/", timeout: TimeSpan.FromDays(1));
            server.AddStaticContent(@"C:\www\public", "/public/", timeout: TimeSpan.FromDays(1));

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}