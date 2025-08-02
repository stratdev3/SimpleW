using System;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NetCoreServer;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // create a context with certificate, support for password protection
            var context = new SslContext(SslProtocols.Tls12, new X509Certificate2(@"C:\Users\SimpleW\ssl\domain.pfx", "password"));

            // pass context to the main SimpleW class
            var server = new SimpleWSServer(context, IPAddress.Any, 2015);

            // serve static content located in your folder "C:\www\spa\" to "/" endpoint
            server.AddStaticContent(@"C:\www\spa\", "/");

            // enable autoindex if no index.html exists in the directory
            server.AutoIndex = true;

            server.Start();

            Console.WriteLine("server started at https://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}