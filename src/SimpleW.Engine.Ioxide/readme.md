# SimpleW.Engine.Ioxide

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW.Engine.Ioxide)](https://www.nuget.org/packages/SimpleW.Engine.Ioxide)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW.Engine.Ioxide)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
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
- serve HTTPS on a dedicated ioxide kTLS port through `ioxide.tls.TlsOptions`
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

`SimpleW.Engine.Ioxide` implements the ioxide kTLS path from `ioxide.tls`. Configure `TlsPort` and the native `ioxide.tls.TlsOptions`; the engine adds that port to `ServerConfig.ExtraPorts`, starts `TlsService` once per reactor, runs `AcceptAsync` for TLS connections, feeds decrypted request bytes into the normal SimpleW pipeline, and keeps response writes as plaintext so kernel TLS encrypts them.

```cs
using System.Net;
using SimpleW;
using SimpleW.Engine.Ioxide;
using ioxide.tls;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseIoxideEngine(config => {
    config.ServerConfig = config.ServerConfig with {
        ReactorCount = Environment.ProcessorCount
    };

    config.TlsPort = 8443;
    config.Tls = new TlsOptions {
        CertificatePath = "/certs/server.crt", // PEM chain
        KeyPath = "/certs/server.key",         // PEM private key
        Alpn = "http/1.1"
    };

    return config;
});

server.MapGet("/", () => new { message = "Hello from SimpleW over ioxide kTLS" });

await server.RunAsync();
```

With this configuration:
- `http://*:8080` remains the plaintext listener.
- `https://*:8443` is served by the ioxide kTLS listener.
- SimpleW handlers, routing, modules, responses, and deferred flush behavior stay the same.
- `session.IsSsl` is true for requests accepted on the TLS port.

The kTLS path intentionally uses PEM paths instead of `SslContext`, because `ioxide.tls` needs the certificate chain and private key as files when `TlsService` starts. It also has the ioxide constraints: TLS 1.3, OpenSSL 3, Linux `tls` kernel module, no session resumption, and kernel transmit encryption.

#### What the engine wires internally

The engine performs the same steps as the raw ioxide example:

```cs
// simplified internal shape
TlsService.Start(reactor, tlsOptions);

if (conn.ListenerPort == config.TlsPort) {
    TlsSession tlsSession = await reactor.GetService<TlsService>().AcceptAsync(conn);
    await server.CreateSessionAsync(new IoxideTlsConnection(conn, tlsSession, endpoint));
}
```

`IoxideTlsConnection` drains plaintext produced during the handshake with `DrainPlaintext()`, decrypts inbound slices with `TlsSession.Decrypt(...)`, and writes responses with `conn.Write(...)` / `conn.FlushAsync()`. Those writes are plaintext from SimpleW's point of view; kTLS emits TLS records on the socket.

#### About SslContext

`SslContext` is not the kTLS configuration surface. It maps naturally to the slower portable `SslStream + ConnectionStream` path, but the implemented ioxide fast path uses the native `TlsOptions` plus `TlsPort`, because kTLS needs `CertificatePath`, `KeyPath`, `Alpn`, and a dedicated listener port.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

This package also keeps a local [changelog](./changelog.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
