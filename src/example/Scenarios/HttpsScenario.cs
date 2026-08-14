using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using SimpleW;
#if NET10_0
using ioxide.tls;
#endif


namespace example;

internal sealed class HttpsScenario : IScenario {

    public string Name => "https";

    public string Description => "HTTPS with an ephemeral self-signed localhost certificate.";

    public string Usage => string.Empty;

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        using RSA rsa = RSA.Create(2048);
        using X509Certificate2 certificate = CreateCertificate(rsa);
        SimpleWServer server;
#if NET10_0
        if (arguments.IoxideEnabled) {
            server = ExampleServer.CreateIoxide(arguments, options => {
                options.Tls = new TlsOptions {
                    CertificatePem = certificate.ExportCertificatePem(),
                    KeyPem = rsa.ExportPkcs8PrivateKeyPem(),
                    Alpn = ["http/1.1"]
                };
            });
        }
        else
#endif
        {
            server = ExampleServer.Create(arguments);
            server.UseEngine(options => {
                options.SslContext = new SslContext(
                    SslProtocols.Tls12 | SslProtocols.Tls13,
                    certificate,
                    clientCertificateRequired: false,
                    checkCertificateRevocation: false
                );
            });
        }
        server.MapGet("/", () => new { message = "Hello HTTPS !", certificate = "ephemeral localhost" });
        server.MapGet("/api/test/hello", () => new { message = "Hello World !" });

        await ExampleServer.RunAsync(server, "https", arguments, cancellationToken).ConfigureAwait(false);
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

}
