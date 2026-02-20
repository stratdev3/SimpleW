# LetsEncrypt

[`SimpleW.Service.Letsencrypt`](https://www.nuget.org/packages/SimpleW.Service.Letsencrypt) provides automatic TLS certificate management for SimpleW using **Let's Encrypt** and the **ACME HTTP-01 challenge**.

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
$ dotnet add package SimpleW.Service.Letsencrypt --version 26.0.0-beta.20260221-1486
```


## Configuration options

| Option | Default | Description |
|---|---|---|
| Domains | `[]` | Domains included in the certificate (SAN). Must be publicly reachable. |
| Email | `null` | Email used for ACME registration. Optional but recommended. |
| StoragePath | `"./letsencrypt"` | Directory where ACME account + cert material is stored. |
| UseStaging | `false` | If `true`, uses the Let's Encrypt **staging** environment (for testing, avoids rate limits). |
| HttpPort | `80` | HTTP port used for **HTTP-01** challenge. Behind a reverse proxy, set this to the **internal port** that actually receives the forwarded challenge requests. |
| HttpsPort | `443` | HTTPS port the server is expected to serve on (used when auto-configuring HTTPS). |
| RenewBefore | `30 days` | Renew when certificate expires in less than this duration. |
| CheckEvery | `12 hours` | How often the renewal loop checks certificate status. |
| PfxPassword | `null` | Optional password used when exporting/storing PFX. |
| AutoConfigureHttps | `true` | If `true`, the module configures SimpleW HTTPS automatically once a certificate is available. |
| Protocols | `Tls12 | Tls13` | Enabled TLS protocols for the configured SSL context. |
| KeyStorageFlags | OS-dependent | How private keys are stored/loaded in `X509Certificate2`. Default is OS-aware (Windows user/machine key store, Linux/macOS ephemeral + exportable). |


## Minimal Example

```csharp
using System.Net;
using SimpleW;
using SimpleW.Service.Letsencrypt;

var server = new SimpleWServer(IPAddress.Any, 443);

server.UseLetsEncryptModule(options => {
    options.Email = "letsenc@simplew.net";
    options.Domains = [ "simplew.net", "www.simplew.net" ];
    options.StoragePath = @"C:\private\simplew.net\letsencrypt";
    // options.UseStaging = true; // for local tests / dry runs (avoids LetsEncrypt production rate limits)
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


## Reverse proxy / forwarded port

If SimpleW is behind a reverse proxy, the proxy must forward :

```
/.well-known/acme-challenge/*
```

And `HttpPort` must match the **internal listening port** that actually receives the forwarded request.

Example:

```
Internet :80 -> Nginx -> SimpleW 192.168.1.2:8080
```

```csharp
options.HttpPort = 8080;
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

- once a valid certificate is available, the module configures SimpleW's HTTPS settings automatically
- TLS protocols are controlled by `Protocols`

If you disable `AutoConfigureHttps`:

- the module still obtains/renews certificates
- you are responsible for loading/applying the certificate to your TLS stack


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
