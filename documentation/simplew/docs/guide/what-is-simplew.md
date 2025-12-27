# What is SimpleW?

SimpleW is a .NET Core library designed for building fast and secure web applications.

<div class="tip custom-block" style="padding-top: 8px">

Just want to try it out? Skip to the [Quickstart](./getting-started).

</div>


## Architecture

SimpleW’s architecture and motivations behind its core design choices :

- **Pure C# (100% managed code)**, running on .NET 8 or later.
- **Built on top of native sockets**, low latency.
- **Compiled delegate**, close to hard-coded method calls.
- **Custom parser**, low memory.
- **Cross‑platform support**, Windows/Linux/Android/MacOS.
- **NuGet package available**, easy to integrate


## Use Cases

- **High‑Performance**

When network efficiency and observability are critical, **SimpleW ensures fast request throughput and low latency**.
Coupled with automatic OpenTelemetry instrumentation, you gain full visibility into request flows, latency, and error rates—enabling you to scale horizontally with confidence.

- **Rapid API Prototype**

When you need to **deliver a proof‑of‑concept API in record time**, SimpleW excels. Its minimal configuration lets you define endpoints in just a few lines of code. With JSON serialization handled automatically and no heavyweight server plumbing to configure, developers can focus on business logic from the very first compile.

- **Embedded Service**

When resources are constrained, SimpleW’s **tiny memory footprint and makes it ideal for lightweight background services**, command‑and‑control agents, or any situation where simplicity and reliability are paramount.

- **static site**

Need to ship a single‑page app or docs alongside your API? With SimpleW you point to a folder (e.g. wwwroot), mount it under a URL, and in milliseconds all your files are served with caching—no heavy framework required. Fast, lean, and ready for your Vue.js dashboard or static site...


## Why this library ?

I believe modern web application architecture should be built around a **REST API acting as a clear contract** between two independent parts.

### The Contract Model

- **Backend (single)** —
The backend is free to evolve, refactor, or even change language (C#, Go, Rust, PHP…).
Its only obligation is to provide and respect the REST API contract.

- **Frontend (one or many)** —
Frontends are also free to evolve independently (SPA, mobile apps, desktop clients…).
They must consume and respect the same REST API contract.

This separation enforces :
- clear boundaries
- independent evolution
- long-term maintainability


### My needs

**Frontend** :

- I mostly build fronteds as [SPAs](https://en.wikipedia.org/wiki/Single-page_application) using [Vite](https://vitejs.dev/), [Vue](https://vuejs.org) and [Vuetify](https://vuetifyjs.com).
- The frontend is a separate project, compiled independently and deployed as static assets.

**Backend** :

- Written in C#, the language I 😍.
- Easy to integrate, lightweight, with a minimal footprint
- Focused on API serving, not full-stack rendering
- Built-in support for : routing, webSockets, server-sent events (SSE)
- No template engine required
- Ability to serve static files (typically the output of `npm run build`)
- Basic observability: request tracing, performance monitoring


### The existings projects

I evaluated and used several existing projects over the years.

- [ASP.NET Core](https://learn.microsoft.com/fr-fr/aspnet/core/?view=aspnetcore-8.0) :
    - too many features I don't need, I don't want _(Razor, Blazor...)_.
    - some behaviors are unnecessarily complex to customize
    - heavy for small or focused APIs
- [IIS](https://iis.net/) an old _« usine à gaz »_ on Windows, Kestrel and SignalR feel similarly heavy on Linux.
- [EmbedIO](https://github.com/unosquare/embedio) : long-time v2 user, the v3 rewrite didn’t fit my expectations. Moreover, it relies on the old Microsoft `HttpListener` and the `websocket-sharp` alternative was not perfect.
- [GenHttp](https://genhttp.org) : feels promising but I was in the process of writting my own.
- __[NetCoreServer](https://github.com/chronoxor/NetCoreServer)__ : Fast, simple, extremly well design, and extendable. Until the v16.0.1, SimpleW was a project on top of the NetCoreServer's `OnReceivedRequest()`


### This project

SimpleW is the result of years of experimentation, production usage, and frustration with existing solutions.

It is a web server designed to be :
- Lightweight
- Explicit
- Fast
- Easy to embed
- Focused on APIs, not full-stack magic

After 4 years in production, SimpleW now :
- Powers multiple APIs
- Handles real-world traffic reliably
- Continues to gain useful features
- Remains simple and predictable

No magic. No bloat. Just what is needed.

## Final Words

SimpleW exists because I needed a server that :
- Matches my architectural beliefs
- Stays out of my way
- Scales without becoming complex

If you find a bug, have an idea, or a missing feature — **feel free to open an issue**.

SimpleW is opinionated, but open.

