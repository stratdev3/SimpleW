# SimpleW.Engine.Ioxide

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![License](https://img.shields.io/badge/license-MIT-7737d1.svg)](https://github.com/Stratdev3/SimpleW/blob/master/licence)
[![NuGet](https://img.shields.io/nuget/v/SimpleW.Engine.Ioxide?color=7737d1&label=version&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Engine.Ioxide)
[![Downloads](https://img.shields.io/nuget/dt/SimpleW.Engine.Ioxide?color=7737d1&label=downloads&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Engine.Ioxide)

<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

`SimpleW.Engine.Ioxide` replaces the default SimpleW socket engine with an `ioxide` transport.
`ioxide` is an io_uring runtime for .NET, see https://mda2av.github.io/ioxide/ if you want to know more.

It lets you:
- run SimpleW on top of the `ioxide` reactor model
- configure the reactor count, server settings, and test mode through `IoxideEngineOptions`
- keep the usual SimpleW routing, modules, handlers, and response APIs
- serve HTTPS through the native Ioxide OpenSSL Raw API
- use a transport designed for Linux/io_uring workloads

### Getting Started

Minimal engine usage:

```cs
using System.Net;
using SimpleW;
using SimpleW.Engine.Ioxide;

namespace Sample {
    class Program {

        static async Task Main() {

            var server = new SimpleWServer(IPAddress.Any, 2015);

            server.UseIoxideEngine(config => {
                config.ServerConfig = config.ServerConfig with {
                    ReactorCount = Environment.ProcessorCount
                };
                return config;
            });

            server.MapGet("/api/test", () => {
                return new { message = "Hello World !" };
            });

            await server.RunAsync();
        }
    }

}
```


### SSL/TLS support

Set `IoxideEngineOptions.Tls` to the native `ioxide.tls.TlsOptions`. TLS then applies to the main SimpleW port and every port already present in `ServerConfig.Tcp.ExtraPorts`. The implementation uses the raw Ioxide OpenSSL API directly: no `SslStream`, `PipeReader` or kTLS.

```cs
using System.Net;
using SimpleW;
using SimpleW.Engine.Ioxide;
using ioxide.tls;

var server = new SimpleWServer(IPAddress.Any, 8443);

server.UseIoxideEngine(config => {
    config.ServerConfig = config.ServerConfig with {
        ReactorCount = Environment.ProcessorCount
    };

    config.Tls = new TlsOptions {
        CertificatePath = "/certs/server.crt", // PEM chain
        KeyPath = "/certs/server.key",         // PEM private key
        Alpn = ["http/1.1"]
    };

    return config;
});

server.MapGet("/", () => new { message = "Hello from SimpleW over ioxide TLS" });

await server.RunAsync();
```

The same options also accept `CertificatePem` and `KeyPem` for in-memory PEM content. Exactly one certificate source and one key source must be configured. OpenSSL 3 is required; the Linux `tls` kernel module is not.

This HTTP/1.x engine requires `Alpn = ["http/1.1"]`. `KernelTx` and `KernelRx` must remain `false`. The resulting transport exposes `session.IsEncrypted`, `NegotiatedApplicationProtocol`, `ClientCertificateSubject` and `IsClientCertificateAuthenticated`.

#### Mutual TLS

Configure client trust anchors to request and validate client certificates:

```cs
config.Tls = new TlsOptions {
    CertificatePath = "/certs/server.crt",
    KeyPath = "/certs/server.key",
    ClientCaPath = "/certs/client-ca.crt",
    RequireClientCertificate = true,
    Alpn = ["http/1.1"]
};
```

`RequireClientCertificate = false` permits anonymous clients but still validates every certificate that is presented. `true` rejects clients without a certificate during the handshake. `ClientCaPem` is the in-memory alternative to `ClientCaPath`.

Manual scenarios:

```shell
curl -k --cert client.crt --key client.key https://127.0.0.1:8443/
curl -k https://127.0.0.1:8443/
curl -k --cert invalid-client.crt --key invalid-client.key https://127.0.0.1:8443/
```

#### What the engine wires internally

The engine performs the same steps as the raw ioxide example:

```cs
TlsService.Start(reactor, tlsOptions);
TlsSession tlsSession = await reactor.GetService<TlsService>().AcceptAsync(conn);
await server.CreateSessionAsync(new IoxideTlsConnection(conn, tlsSession, endpoint));
```

`IoxideTlsConnection` drains plaintext produced during the handshake, decrypts inbound slices with `TlsSession.Decrypt(...)`, immediately returns ciphertext buffers to the ring, and retains plaintext fragments in one pooled buffer. Responses go through `TlsSession.Write(...)`, which encrypts directly into the Ioxide write slab, followed by the normal deferred flush.

#### About SslContext

`SslContext` configures only the default Socket engine. The Ioxide engine uses native `TlsOptions` because OpenSSL needs PEM certificate, key and client trust-anchor data.

Ioxide 0.4.184 does not expose disposal for `TlsService`, its `SSL_CTX` or its pinned ALPN state. For that reason, a TLS-enabled `IoxideEngine` is intentionally single-start: listener reload and a second start of the same engine are rejected. Create the TLS configuration before the initial server start.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

This package also keeps a local [changelog](./changelog.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
