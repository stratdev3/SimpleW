# Ioxide <Badge type="tip" text="Official" />

[`SimpleW.Engine.Ioxide`](https://www.nuget.org/packages/SimpleW.Engine.Ioxide) replaces the default Socket engine with the Linux-native io_uring runtime while keeping the SimpleW routing, module, handler, response, SSE, and WebSocket APIs.

The current engine supports HTTP/1.x over plaintext TCP or native OpenSSL TLS. It does not use `Socket`, `SslStream`, `PipeReader`, or kTLS on its connection hot paths.

::: tip NOTE
For all information about ioxide, please refer to the [official website](https://mda2av.github.io/ioxide/).
:::


## Requirements

- **Linux** and a kernel supported by Ioxide
- **.NET 10**;
- an `IPEndPoint` using `IPAddress.Any` or `IPAddress.IPv6Any`;
- OpenSSL 3 when TLS is enabled.

`IPAddress.Any` creates IPv4 listeners. `IPAddress.IPv6Any` enables Ioxide dual-stack listeners. Specific local IP addresses are rejected because Ioxide binds wildcard listeners.


## Installation

```shell
dotnet add package SimpleW.Engine.Ioxide
```


## Minimal server

```csharp
using System.Net;
using SimpleW;
using SimpleW.Engine.Ioxide;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.UseIoxideEngine();

server.MapGet("/", () => new {
    message = "Hello from SimpleW over Ioxide"
});

await server.RunAsync();
```

The main port always comes from `SimpleWServer.EndPoint`. Any `ServerConfig.Tcp.Port` value is replaced during startup. Other TCP settings and `ExtraPorts` are preserved. UDP and QUIC are disabled by this HTTP/1.x engine.


## Configuration

Use the options callback to configure the native Ioxide server:

```csharp
using ioxide;

server.UseIoxideEngine(options => {
    options.ServerConfig = options.ServerConfig with {
        ReactorCount = 12,          // force 12 reactors, default is Environment.ProcessorCount
        RingEntries = 8192,         // SQ/CQ depth per ring
        RecvBufferSize = 32 * 1024, // bytes per shared recv buffer
        RecvSlots = 4096,           // shared recv buffer-ring depth
        Incremental = null,         // per-connection recv rings (6.12+) - see Tcp/Incremental
        Tcp = options.ServerConfig.Tcp! with {
            ExtraPorts = [8081],        // extra listener ports (one handler, several doors)
            ListenBacklog = 2048,       // accept-queue depth per SO_REUSEPORT listener
            WriteSlabSize = 16 * 1024   // per-connection write buffer before overflow kicks in
        }
    };
});
```

You can also pass a pre-built `ServerConfig` directly. A callback can run once on every reactor thread after native services have initialized and before the reactor reports itself ready:

```csharp
var options = new IoxideEngineOptions();

server.UseIoxideEngine(
    options,
    _ => Console.WriteLine($"{Thread.CurrentThread.Name} is ready")
);
```

Startup completes only after every reactor has opened its ring, initialized TLS when configured, and completed this callback. An exception raised during initialization fails the whole startup and stops the reactors already created.


## Reactor and ring tuning

`IoxideEngineOptions` defaults to `Environment.ProcessorCount` reactors. Each reactor owns one thread, one io_uring instance, and its receive buffers. One reactor per logical processor is a useful throughput-oriented starting point, but it can be slower when the number of active connections is low relative to the number of rings.

For example, a benchmark with approximately 200 connections on a machine exposing many logical processors should also be measured with 8 or 12 reactors. Fewer reactors can improve batching and reduce memory consumption. There is no universal best value: benchmark the real response sizes, connection count, TLS mode, and hardware.

### Shared receive ring

The shared ring is the default and is selected with `Incremental = null`:

```csharp
options.ServerConfig = options.ServerConfig with {
    ReactorCount = 12,
    RecvSlots = 4096,
    RecvBufferSize = 32 * 1024,
    Incremental = null
};
```

Its reserved receive-buffer memory is approximately:

```text
ReactorCount × RecvSlots × RecvBufferSize
```

With 4096 slots of 32 KiB, that is approximately 128 MiB per reactor before connection slabs and other native allocations.


### Incremental receive rings

Incremental rings require Linux 6.12 or later and allocate per-connection receive rings:

```csharp
options.ServerConfig = options.ServerConfig with {
    ReactorCount = 12,
    Incremental = new IncrementalOptions {
        MaxConnections = 1024,
        RecvSlots = 8,
        RecvBufferSize = 16 * 1024
    }
};
```

Their upper-bound reservation follows the Ioxide configuration model:

```text
ReactorCount × MaxConnections × RecvSlots × RecvBufferSize
```

Size these values deliberately; incremental mode can reserve substantially more native memory than a shared ring.


## Flush policy

`IoxideFlushPolicy.EndOfReadBatch` is the default. It stages writes produced while processing the current read batch and performs one Ioxide flush, which is important for HTTP pipelining and small response fragments.

```csharp
server.UseIoxideEngine(options => {
    options.FlushPolicy = IoxideFlushPolicy.EndOfReadBatch;
});
```

Use `Immediate` only when every completed write must be flushed independently:

```csharp
options.FlushPolicy = IoxideFlushPolicy.Immediate;
```

Both modes retain the transport single-writer guarantee. The deferred-flush feature is also used by streaming responses, SSE, and WebSocket traffic.


## OpenSSL Raw TLS

Set `IoxideEngineOptions.Tls` to native `ioxide.tls.TlsOptions`. TLS then applies to the main SimpleW port and every port in `ServerConfig.Tcp.ExtraPorts`; this version does not mix HTTP and HTTPS listeners in one engine.

```csharp
using System.Net;
using ioxide.tls;
using SimpleW;
using SimpleW.Engine.Ioxide;

var server = new SimpleWServer(IPAddress.Any, 8443);

server.UseIoxideEngine(options => {
    options.Tls = new TlsOptions {
        CertificatePath = "/certs/server.crt",
        KeyPath = "/certs/server.key",
        Alpn = ["http/1.1"],
        KernelTx = false,
        KernelRx = false
    };
});

server.MapGet("/", () => new {
    message = "Hello from SimpleW over Ioxide TLS"
});

await server.RunAsync();
```

`CertificatePem` and `KeyPem` accept PEM content held in memory. Configure exactly one certificate source and one key source.

This engine accepts only `Alpn = ["http/1.1"]`. HTTP/2 is rejected. `KernelTx` and `KernelRx` must remain `false`: OpenSSL encrypts and decrypts in userspace, and the Linux `tls` kernel module is not required.

Internally, every reactor starts a `TlsService` before accepting connections. Each accepted connection completes `AcceptAsync`, drains plaintext produced during the handshake, decrypts ciphertext with `TlsSession.Decrypt`, and encrypts responses with `TlsSession.Write`. Ciphertext receive buffers are returned to Ioxide immediately; retained plaintext uses one reusable `ArrayPool<byte>` buffer per connection.


## Client certificates and TLS metadata

When the referenced Ioxide package exposes its client-certificate API, configure a client CA to request and validate client certificates:

```csharp
options.Tls = new TlsOptions {
    CertificatePath = "/certs/server.crt",
    KeyPath = "/certs/server.key",
    ClientCaPath = "/certs/client-ca.crt",
    RequireClientCertificate = true,
    Alpn = ["http/1.1"]
};
```

`ClientCaPem` is the in-memory alternative to `ClientCaPath`. With client anchors configured, `RequireClientCertificate = false` allows clients that do not present a certificate but validates every certificate that is presented. `true` rejects missing or invalid client certificates during the TLS handshake.

After a successful handshake, SimpleW exposes the common metadata on `HttpSession`:

```csharp
server.MapGet("/identity", (HttpSession session) => new {
    alpn = session.NegotiatedApplicationProtocol,
    subject = session.ClientCertificateSubject,
    authenticated = session.IsClientCertificateAuthenticated,
    email = session.ClientCertificateEmailAddress
});
```

- `NegotiatedApplicationProtocol` comes from `TlsSession.NegotiatedAlpn`;
- `ClientCertificateSubject` comes from `TlsSession.PeerSubject` when available;
- `IsClientCertificateAuthenticated` is true when a validated peer subject is available;
- `ClientCertificateEmailAddress` is always `null` because Ioxide does not expose it separately.

The subject uses OpenSSL's native validated representation. Do not parse it naively or compare it directly with a subject formatted by the .NET Socket engine.

::: warning Ioxide 0.4.184 package surface
The `ioxide` 0.4.184 DLL currently restored by this repository does not expose `TlsSession.PeerSubject`, `TlsOptions.ClientCaPath`, or `TlsOptions.RequireClientCertificate`, although they exist in the newer documentary source snapshot. `SimpleW.Engine.Ioxide` therefore uses a cached compatibility accessor: the subject is `null` and authentication is `false` when the runtime package lacks `PeerSubject`. Actual mTLS configuration requires an Ioxide NuGet release that publishes these APIs.
:::


## Manual TLS checks

The following commands are useful after starting a configured server:

```shell
# Server certificate only
curl -k --http1.1 https://127.0.0.1:8443/

# Valid client certificate
curl -k --http1.1 --cert client.crt --key client.key https://127.0.0.1:8443/identity

# Invalid client certificate
curl -k --http1.1 --cert invalid-client.crt --key invalid-client.key https://127.0.0.1:8443/identity

# Inspect the handshake and certificate request
openssl s_client -connect 127.0.0.1:8443 -alpn http/1.1 -cert client.crt -key client.key
```


## Shutdown, restart, and reload

Stopping the engine first asks SimpleW to close all active sessions, then stops and joins every reactor thread. Ioxide remains responsible for closing and recycling its native connection descriptors.

A plaintext `IoxideEngine` can be started again after it has stopped. A TLS-enabled instance is intentionally single-start: Ioxide 0.4.184 does not expose disposal for `TlsService`, its `SSL_CTX`, or its pinned ALPN state. Restarting or reloading that same TLS engine instance is rejected. Create a new engine instance for a new process lifecycle.

Because Ioxide does not separate listener shutdown from ring shutdown, stopping or reloading the engine closes existing keep-alive, SSE, and WebSocket connections.


## Transport behavior

The plaintext transport follows the Ioxide TCP Raw model:

- receive buffers are exposed to SimpleW without copying;
- fragmented requests and unconsumed bytes are retained between reads;
- multiple relative `AdvanceTo` calls are accumulated before buffers are returned;
- pipelined requests can share one read batch and one flush;
- writes use the native per-connection slab and enforce a single writer;
- connection shutdown leaves descriptor ownership and recycling to Ioxide.

The TLS transport is separate so plaintext connections do not pay for TLS branches or TLS state.
