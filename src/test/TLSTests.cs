using System;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for TLS
    /// </summary>
    public class TLSTests {

        #region tls

        private HttpClientHandler httpClientHandler = new HttpClientHandler {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };


        [Fact]
        public async Task MapGet_SSL_HelloWorld() {

            using RSA rsa = RSA.Create(2048);
            using X509Certificate2 certificate = CreateCertificate(rsa);

            // create a context with certificate, support for password protection
            SslContext sslContext = new(
                SslProtocols.Tls12 | SslProtocols.Tls13,
                certificate,
                clientCertificateRequired: false,
                checkCertificateRevocation: false
            );

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseEngine(options => {
                options.SslContext = sslContext;
            });
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient(httpClientHandler);
            var response = await client.GetAsync($"https://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            await server.StopAsync();
        }

        [Fact]
        public async Task MapGet_SSL_Should_Expose_Client_Certificate_Metadata() {

            using RSA serverRsa = RSA.Create(2048);
            using X509Certificate2 serverCertificate = CreateCertificate(serverRsa);
            using RSA clientRsa = RSA.Create(2048);
            using X509Certificate2 clientCertificate = CreateClientCertificate(clientRsa);

            SslContext sslContext = new(
                SslProtocols.Tls12 | SslProtocols.Tls13,
                serverCertificate,
                clientCertificateRequired: true,
                checkCertificateRevocation: false,
                clientCertificateValidation: (_, remoteCertificate, _, _) =>
                    remoteCertificate != null &&
                    remoteCertificate.GetRawCertData().AsSpan().SequenceEqual(clientCertificate.RawData)
            );

            bool? isEncrypted = null;
            string? clientCertificateSubject = null;
            string? clientCertificateEmailAddress = null;
            bool? isClientCertificateAuthenticated = null;

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseEngine(options => {
                options.SslContext = sslContext;
            });
            server.MapGet("/", (HttpSession session) => {
                isEncrypted = session.IsEncrypted;
                clientCertificateSubject = session.ClientCertificateSubject;
                clientCertificateEmailAddress = session.ClientCertificateEmailAddress;
                isClientCertificateAuthenticated = session.IsClientCertificateAuthenticated;
                return new { message = "Hello mTLS !" };
            });

            await server.StartAsync();
            try {
                using HttpClientHandler handler = new() {
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
                handler.ClientCertificates.Add(clientCertificate);

                using HttpClient client = new(handler);
                using HttpResponseMessage response = await client.GetAsync($"https://{server.Address}:{server.Port}/");

                Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
                Check.That(isEncrypted).IsEqualTo(true);
                Check.That(clientCertificateSubject).IsEqualTo(clientCertificate.Subject);
                Check.That(clientCertificateEmailAddress).IsEqualTo("client@simplew.test");
                Check.That(isClientCertificateAuthenticated).IsEqualTo(true);
            }
            finally {
                await server.StopAsync();
            }
        }

        private static X509Certificate2 CreateCertificate(RSA rsa) {
            CertificateRequest request = new(
                "CN=localhost, O=SimpleW, OU=Development",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            SubjectAlternativeNameBuilder alternativeNames = new();
            alternativeNames.AddDnsName("localhost");
            alternativeNames.AddIpAddress(IPAddress.Loopback);
            alternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(alternativeNames.Build());

            DateTimeOffset now = DateTimeOffset.UtcNow;

            using X509Certificate2 generated = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(30));
            byte[] pfx = generated.Export(X509ContentType.Pkcs12, string.Empty);

#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pfx, string.Empty, X509KeyStorageFlags.DefaultKeySet);
#else
            return new X509Certificate2(pfx, string.Empty, X509KeyStorageFlags.DefaultKeySet);
#endif
        }

        private static X509Certificate2 CreateClientCertificate(RSA rsa) {
            CertificateRequest request = new(
                "CN=client, O=SimpleW",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") },
                critical: true
            ));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

            SubjectAlternativeNameBuilder alternativeNames = new();
            alternativeNames.AddEmailAddress("client@simplew.test");
            request.CertificateExtensions.Add(alternativeNames.Build());

            DateTimeOffset now = DateTimeOffset.UtcNow;
            using X509Certificate2 generated = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(30));
            byte[] pfx = generated.Export(X509ContentType.Pkcs12, string.Empty);

#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pfx, string.Empty, X509KeyStorageFlags.DefaultKeySet);
#else
            return new X509Certificate2(pfx, string.Empty, X509KeyStorageFlags.DefaultKeySet);
#endif
        }

        #endregion tls

    }

}
