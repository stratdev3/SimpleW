# Ioxide Engine <Badge type="tip" text="Official" />

The [`SimpleW.Engine.Ioxide`](https://www.nuget.org/packages/SimpleW.Engine.Ioxide) package replaces the default SimpleW socket engine with an `ioxide` transport.

`ioxide` is an io_uring runtime for .NET. The addon keeps the SimpleW routing, modules, handlers, responses, and server-level configuration model, while moving accepted connections to the ioxide reactor model.


## SSL / TLS support

`SimpleW.Engine.Ioxide` implements the ioxide kTLS path from `ioxide.tls`.

Configure `TlsPort` and the native `ioxide.tls.TlsOptions`. The engine then:
- adds the TLS port to `ServerConfig.ExtraPorts`;
- starts `TlsService` once per reactor;
- runs `AcceptAsync` for connections accepted on the TLS port;
- drains plaintext already produced during the handshake;
- decrypts inbound TLS records before SimpleW parses the request;
- writes responses as plaintext so kernel TLS emits the TLS records.


## kTLS example

```csharp
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

This serves plaintext HTTP on the server port and HTTPS on the configured TLS port:
- `http://*:8080` uses the normal ioxide transport.
- `https://*:8443` uses `ioxide.tls` / kTLS.
- `session.IsSsl` is true on TLS requests.


## Internal model

The addon follows the ioxide TLS model directly:

```csharp
TlsService.Start(reactor, tlsOptions);

if (conn.ListenerPort == config.TlsPort) {
    TlsSession tlsSession = await reactor.GetService<TlsService>().AcceptAsync(conn);
    await server.CreateSessionAsync(new IoxideTlsConnection(conn, tlsSession, endpoint));
}
```

`IoxideTlsConnection` is the bridge into SimpleW:
- it feeds `TlsSession.DrainPlaintext()` into the request stream before reading again;
- it decrypts inbound buffers with `TlsSession.Decrypt(item.Ptr, item.Len)`;
- it returns ioxide receive buffers after decrypting them;
- it writes plaintext responses to the connection, letting kTLS encrypt outbound records.


## Constraints

The kTLS path has the constraints of `ioxide.tls`:
- TLS 1.3 only;
- OpenSSL 3;
- Linux `tls` kernel module;
- PEM certificate chain and PEM private key files;
- no session resumption.

`SslContext` is not used for this fast path. It represents the portable `SslStream + ConnectionStream` shape, while kTLS needs `TlsPort` plus `TlsOptions.CertificatePath`, `TlsOptions.KeyPath`, and `TlsOptions.Alpn`.