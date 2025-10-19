﻿using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for SSL
    /// </summary>
    public class SSLTests {

        #region ssl

        // echo subjectAltName=DNS:localhost,IP:127.0.0.1 > san.cnf && openssl genrsa -out privkey.pem 2048 && openssl req -new -key privkey.pem -out cert.csr -subj "/C=FR/O=SimpleW/OU=Dev/CN=localhost/emailAddress=admin@localhost" && openssl x509 -req -days 1095 -in cert.csr -signkey privkey.pem -out cert.pem -extfile san.cnf && openssl pkcs12 -export -out simplew.pfx -inkey privkey.pem -in cert.pem -password pass:secret && del san.cnf
        private const string SslCertificateBase64 = "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";

        private HttpClientHandler httpClientHandler = new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };


        [Fact]
        public async Task MapGet_SSL_HelloWorld() {

            string certificateFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "MapGet_SSL_HelloWorld", "simplew.pfx");
            Directory.CreateDirectory(Path.Combine(certificateFilePath, ".."));
            File.WriteAllBytes(certificateFilePath, Convert.FromBase64String(SslCertificateBase64));

            // create a context with certificate, support for password protection
            var context = new NetCoreServer.SslContext(SslProtocols.Tls12, new X509Certificate2(certificateFilePath, "secret"));

            // server
            var server = new SimpleWSServer(context, IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient(httpClientHandler);
            var response = await client.GetAsync($"https://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        #endregion ssl

    }

}
