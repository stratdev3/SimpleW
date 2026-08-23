# LetsEncrypt <Badge type="tip" text="Official" />

The [`SimpleW.Service.Letsencrypt`](https://www.nuget.org/packages/SimpleW.Service.Letsencrypt) package provides automatic TLS certificate management for SimpleW using **Let's Encrypt** and the **ACME HTTP-01 challenge**. It is implemented as a SimpleW middleware.

Its goal is simple: enable HTTPS with real certificates, keep them renewed automatically, and stay minimal and predictable — fully aligned with SimpleW's philosophy.


## Features

It allows you to :
- Automatically request TLS certificates from Let's Encrypt
- Handle **HTTP-01 challenges** internally
- Store and load certificates from disk
- Automatically renew certificates before expiration
- Configure certificate storage behavior (OS-aware)
- Enable HTTPS without external tooling (certbot, nginx, etc.)


## Requirements

- .NET 8.0
- SimpleW (core server)
- Certes (automatically included)
- Publicly reachable domains
- Port 80 reachable from the Internet (directly or via proxy)
- DNS records pointing to the server


## Installation

```sh
$ dotnet add package SimpleW.Service.Letsencrypt
```

See the [changelog](./service-letsencrypt-changelog.md)


## Configuration options

| Option | Default | Description |
|---|---|---|
| Domains | `[]` | Domains included in the certificate (SAN). Must be publicly reachable. |
| Email | `null` | Email used for ACME registration. Optional but recommended. |
| StoragePath | `"./letsencrypt"` | Directory where ACME account + cert material is stored. |
| UseStaging | `false` | If `true`, uses the Let's Encrypt **staging** environment to validate the ACME setup without consuming production rate limits. |
| HttpPort | `80` | HTTP port used for **HTTP-01** challenge. Behind a reverse proxy, set this to the **internal port** that actually receives the forwarded challenge requests. |
| HttpsPort | `443` | HTTPS port the server is expected to serve on (used when auto-configuring HTTPS). |
| RenewBefore | `30 days` | Renew when certificate expires in less than this duration. |
| CheckEvery | `12 hours` | How often the renewal loop checks certificate status. |
| PfxPassword | `null` | Optional password used when exporting/storing PFX. |
| AutoConfigureHttps | `true` | If `true`, the module calls `OnEngineHttpsEnable` / `OnEngineHttpsDisable` to configure HTTPS once a certificate is available. |
| OnEngineHttpsEnable | Default `SimpleWEngine` callback | Called with `(SimpleWServer server, X509Certificate2 certificate)` to enable HTTPS on the selected engine. |
| OnEngineHttpsDisable | Default `SimpleWEngine` callback | Called with `(SimpleWServer server)` to disable HTTPS before HTTP-01 challenge handling. |
| Protocols | `Tls12 | Tls13` | Enabled TLS protocols used by the default `SimpleWEngine` callback. |
| KeyStorageFlags | OS-dependent | How private keys are stored/loaded in `X509Certificate2`. Default is OS-aware (Windows user/machine key store, Linux/macOS ephemeral + exportable). |


## Staging and production environments

`UseStaging` only selects the Let's Encrypt ACME environment. It does not make the HTTP-01 challenge local or optional.

Both staging and production require:

- Public DNS records pointing to the server
- Public port 80 to reach the HTTP listener, directly or through a reverse proxy or port forwarding
- `/.well-known/acme-challenge/*` requests to be forwarded to SimpleW

Staging performs the complete ACME issuance flow, but its certificates are signed by a test certificate authority that is not trusted by browsers and standard clients. A browser warning or a TLS trust error is therefore expected with a staging certificate. Use staging to validate DNS, network routing, and ACME configuration without consuming production rate limits; use production for publicly trusted certificates.

The module option defaults to production (`UseStaging = false`). The `letsencrypt` scenario in the example application deliberately uses staging by default: omit `--production` for staging, or pass `--production` to use the production environment.

Use a different `StoragePath` for each environment, for example `./letsencrypt-staging` and `./letsencrypt-production`. The module reuses a valid certificate already present in its storage directory without checking which ACME environment issued it. Sharing a directory can therefore cause a staging certificate to be reused after switching to production.


## Minimal example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Letsencrypt;

var server = new SimpleWServer(IPAddress.Any, 443);

server.UseLetsEncryptModule(options => {
    options.Email = "letsenc@simplew.net";
    options.Domains = [ "simplew.net", "www.simplew.net" ];
    options.StoragePath = @"C:\private\simplew.net\letsencrypt";
    // options.UseStaging = true; // validate the ACME setup without consuming production rate limits
});

await server.RunAsync();
```


## HTTP-01 challenge

Let's Encrypt will call :

```
http://<domain>/.well-known/acme-challenge/<token>
```

The module serves this path internally.

Requests are handled **before routing**.

The validation request always arrives on the public port 80, including when using staging. If SimpleW listens on another internal port, configure port forwarding or a reverse proxy and set `HttpPort` to the internal SimpleW port.


## Reverse proxy / forwarded port

If SimpleW is behind a reverse proxy, the proxy must forward :

```
/.well-known/acme-challenge/*
```

And `HttpPort` must match the **internal listening port** that actually receives the forwarded request.

Likewise, when the public HTTPS port differs from the SimpleW listener port, forward public port 443 to the internal port configured by `HttpsPort`.

Example:

```
Internet :80 -> Nginx -> SimpleW 192.168.1.2:8080
Internet :443 -> Nginx -> SimpleW 192.168.1.2:4443
```

```csharp
options.HttpPort = 8080;
options.HttpsPort = 4443;
```


## Renewal strategy

Renewal is handled by an **async loop**:

- no timers
- no overlapping executions
- predictable cancellation on shutdown

The loop:

- checks certificate expiration every `CheckEvery`
- renews when expiration is within `RenewBefore`


## TLS behavior

If `AutoConfigureHttps` is enabled:

- once a valid certificate is available, the module calls `OnEngineHttpsEnable(server, certificate)`;
- before HTTP-01 challenge handling, the module calls `OnEngineHttpsDisable(server)`;
- by default, those callbacks configure the default `SimpleWEngine` using `X509Certificate2` and `SslContext`;
- TLS protocols are controlled by `Protocols` only for the default `SimpleWEngine` callback.

If you use another engine, override the callbacks:

```csharp
server.UseLetsEncryptModule(options => {
    options.OnEngineHttpsEnable = (server, certificate) => {
        // Convert/apply X509Certificate2 for your engine.
    };

    options.OnEngineHttpsDisable = server => {
        // Disable HTTPS for your engine.
    };
});
```

If you disable `AutoConfigureHttps`:

- the module still obtains/renews certificates;
- you are responsible for loading/applying the certificate to your TLS stack.


## Key storage behavior

`KeyStorageFlags` controls how the private key is stored/loaded.

The default is OS-aware:

- Windows:
  - interactive: `UserKeySet | PersistKeySet`
  - service: `MachineKeySet | PersistKeySet`
- Linux / macOS:
  - `EphemeralKeySet | Exportable`

Override only if you know exactly why.


## Telemetry & Counter

The LetsEncrypt module can optionally emit **telemetry and metrics** to help you observe certificate status and renewal behavior in production. 

Telemetry is fully optional and disabled by default, and also relies on the global SimpleW telemetry system.

Example :

```csharp
var server = new SimpleWServer(IPAddress.Any, 443);

// global simplew telemetry, must be enabled
server.EnableTelemetry();

server.UseLetsEncryptModule(options => {
    options.Domains = [ "simplew.net", "www.simplew.net" ];
    options.Email = "admin@simplew.net";
    options.EnableTelemetry = true; // local enable letsencrypt telemetry
});

```

### What is tracked

The LetsEncrypt module exposes **observable gauges** describing the currently loaded certificate :

- `simplew.letsencrypt.certificate.remaining_days` : Remaining number of days before certificate expiration.
- `simplew.letsencrypt.certificate.loaded` : Indicates whether a certificate is currently loaded.
