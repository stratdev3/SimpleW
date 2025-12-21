using System;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // create a certificate
#if NET9_0_OR_GREATER
            X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(@"C:\Users\SimpleW\ssl\domain.pfx", "password");
#else
            X509Certificate2 cert = new(@"C:\Users\SimpleW\ssl\domain.pfx", "password");
#endif

            // create a context with certificate, support for password protection
            var context = new SslContext(SslProtocols.Tls12 | SslProtocols.Tls13, cert, clientCertificateRequired: false, checkCertificateRevocation: false);

            // assign context
            server.UseHttps(context);

            // serve static content
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\spa\";                  // serve your files located here
                options.Prefix = "/";                           // to "/" endpoint
                options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
                options.AutoIndex = true;                       // enable autoindex if no index.html exists in the directory
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            await server.RunAsync();
        }
    }
}