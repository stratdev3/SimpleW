using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve static content located in your folder "C:\www\spa\" to "/" endpoint
            server.AddStaticContent(@"C:\www\spa\", "/");

            // enable autoindex if no index.html exists in the directory
            server.AutoIndex = true;

            // start non blocking background server
            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}