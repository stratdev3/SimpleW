# Frequently Asked Questions

Clear answers about what SimpleW is, where it fits, and what to expect when using it in production.

::: tip Looking for code?
Start with the [Getting Started guide](../guide/getting-started.md), then explore the [available addons](../addons/addons.md).
:::


## Choosing SimpleW

### What exactly is SimpleW?

SimpleW is a lightweight HTTP server for .NET. It provides routing, handlers, controllers, static files, WebSockets, Server-Sent Events, logging, and observability without depending on ASP.NET.

You can run it as a dedicated web service or embed it directly into an existing .NET application.


### Is SimpleW a web server or a web framework?

Primarily, it is a web server with a small application model around it.

The core owns the network listener, HTTP parsing, routing, request lifecycle, and response writing. Optional addons provide higher-level capabilities such as authentication, firewall rules, automatic certificates, background jobs, Swagger, and alternative engines.


### Does SimpleW replace ASP.NET Core?

No. It offers a different trade-off.

ASP.NET Core provides a very large, integrated ecosystem. SimpleW focuses on a smaller runtime, explicit configuration, embeddability, and code that remains practical to understand from end to end.

Choose the tool whose scope matches the application.


### When is SimpleW a good fit?

SimpleW is particularly useful for:

- Standalone APIs and small web services
- HTTP endpoints embedded in desktop, mobile, industrial, or server applications
- Real-time services using WebSockets or Server-Sent Events
- Internal tools and administration endpoints
- Applications that need direct control over their HTTP runtime
- Custom platforms that want a small core and optional features


### When should I choose another stack?

Another stack may be a better fit when the application depends heavily on ASP.NET-specific middleware, Identity, Blazor or third-party components built exclusively for that ecosystem.


### Is SimpleW production-ready?

The core has been used in production for five years to serve real APIs and traffic. Its design favors explicit behavior, bounded resources, observability, and predictable lifecycle management.

Production readiness still depends on the complete application: configuration, authentication, TLS, monitoring, deployment, backups, and operational practices remain your responsibility.


## Architecture

### Does SimpleW require ASP.NET or the ASP.NET runtime?

No. SimpleW runs on the standard .NET runtime and does not depend on ASP.NET.

It can still run alongside ASP.NET components in the same broader system when an application needs both.


### What does "zero-dependency core" mean?

The `SimpleW` core package does not pull in ASP.NET or a collection of framework packages. It provides its own socket-based server, HTTP parser, router, and request pipeline using the .NET runtime.

Features that do not belong in every application are kept in separate packages.


### What is the difference between the Core and Addons?

The Core contains the stable primitives required to receive and process HTTP requests.

[Addons](../addons/addons.md) extend that core without changing its structure. You install only what the application needs: authentication, firewall, Let's Encrypt, background tasks, Swagger, templates, logging integrations, dependency injection, or another network engine.


### Can I embed SimpleW in an existing application?

Yes. Create a `SimpleWServer`, register the required routes or modules, and start it as part of the application's lifecycle.

This works well when an existing process needs a health endpoint, administration API, webhook receiver, local interface, or complete HTTP service without becoming an ASP.NET application.


### Should I use route delegates or controllers?

Use route delegates for small APIs, prototypes, and endpoints that remain easy to understand inline.

Use controllers when the route count grows or when the application benefits from shared routes, attributes, authorization metadata, and a clearer separation between endpoints. Both styles use the same router and can coexist.


### Is dependency injection required?

No. SimpleW does not require a container and keeps object creation explicit by default.

Applications that need dependency injection can add it through the [Dependency Injection helper](../addons/helper-dependency-injection.md) without adding that model to the core.


### Can core components be replaced?

Yes. The network engine, router, JSON engine, result handler, principal resolver, and other integration points can be configured or replaced.

See [Replacing Core Components](../guide/replacing-core-components.md) for the supported extension points.


## Protocols and Features

### Which HTTP versions are supported?

SimpleW supports HTTP/1.x, including persistent connections and pipelined requests.

WebSockets are supported through the HTTP upgrade flow, and Server-Sent Events are available for one-way event streams.


### Does SimpleW support HTTPS?

Yes. You can provide your own certificate and configure TLS directly, including mutual TLS when client certificates are required.

The [Let's Encrypt addon](../addons/service-letsencrypt.md) can request, store, activate, and renew public certificates automatically.


### What authentication options are available?

Official packages cover [Basic authentication](../addons/service-basicauth.md), [JWT](../addons/service-jwt.md), and [OpenID Connect](../addons/service-openid.md). Mutual TLS can also map authenticated client certificates to a SimpleW principal.

Authentication establishes identity. Authorization can then be enforced through handler metadata, middleware, roles, or application-specific policies.


### Can SimpleW serve static files and a frontend application?

Yes. The built-in static-files module supports caching, range requests, multiple directories, and SPA-style fallback scenarios.

See [Serving Static Files](../guide/staticfiles.md) for configuration examples.


### Does SimpleW provide logs, traces, and metrics?

Yes. Logging is part of the core, and telemetry can emit traces and metrics through the observability integration.

Telemetry is opt-in and configurable per server instance. See [Observability](../guide/observability.md) and [Logging](../guide/logging.md).


## Deployment and Operations

### Do I need a reverse proxy?

Not necessarily. SimpleW can listen directly on an HTTP or HTTPS endpoint.

A reverse proxy remains useful when you need advanced load balancing or one public entry point for several services.

When trusting forwarded headers, only accept them from a proxy you control and configure the client IP resolver accordingly.


### Is SimpleW fast?

Performance is a core design concern: the server uses native sockets, a custom HTTP parser, compiled handlers, and an allocation-aware request lifecycle.

Real performance still depends on the workload, serialization, middleware, logging, TLS, and deployment environment. Use the published [performance measurements](../guide/performances.md) as a reference, then benchmark the actual application under representative traffic.


### How are breaking changes handled?

SimpleW does not strictly follow semantic versioning and prioritizes a clean design over preserving technical debt indefinitely.

Breaking changes are documented in the [changelog](./changelog.md), with migration guides provided for significant upgrades. Production applications should review these notes before updating and validate the new version in their own environment.


### Is there an LTS release or support for old versions?

There is currently no separate LTS channel and no promise of backports for older major versions. The project focuses its maintenance effort on the latest release.


## Project and Support

### Is SimpleW open source?

Yes. SimpleW and its official addons are released under the MIT License. The source is available on [GitHub](https://github.com/stratdev3/SimpleW).


### Where can I ask a question?

Join the [SimpleW Discord server](https://discord.gg/mDNRjyV8Ak) for usage questions, design discussions, and feedback.

Before asking, include the SimpleW version, operating system, relevant configuration, a minimal code sample, and the observed behavior. Good context usually turns a long investigation into a short answer.


### How can I contribute?

Start with a discussion before investing in a large change. Small reproductions, documentation corrections, focused bug reports, and clearly scoped proposals are especially useful.

Contributions should remain compatible with the project's MIT licensing and its preference for simple, explicit implementations.
