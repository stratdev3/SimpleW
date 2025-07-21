# What is SimpleW?

SimpleW is a .NET Core library designed for building fast and secure web applications.
It is based in the the great [NetCoreServer](https://github.com/chronoxor/NetCoreServer) socket server.

<div class="tip custom-block" style="padding-top: 8px">

Just want to try it out? Skip to the [Quickstart](./getting-started).

</div>


## Architecture

SimpleW’s architecture and motivations behind its core design choices :

- **Pure C# (100% managed code)**, running on .NET 8 or later.
- **Built on top of native sockets**, no `HttpListener` inside.
- **Compiled delegate**, close to hard-coded method calls.
- **Cross‑platform support**, Windows/Linux/Android/macOS.
- **Single dependency**: `Newtonsoft.Json` for serialization/deserialization
- **NuGet package available**, easy to integrate


## Use Cases

- **High‑Performance Microservices**

In a distributed microservice architecture, network efficiency and observability are critical. SimpleW’s native socket server delivers minimal network overhead, ensuring fast request throughput and low latency. With built‑in WebSocket capabilities, it can handle real‑time bidirectional communication without additional libraries. Coupled with automatic OpenTelemetry instrumentation, you gain full visibility into request flows, latency, and error rates—enabling you to scale horizontally with confidence.

- **static site**

Need to ship a single‑page app or docs alongside your API? With SimpleW you point to a folder (e.g. wwwroot), mount it under a URL, and in milliseconds your HTML, CSS, JS and images are served with correct MIME types and smart caching—no heavy framework required. Fast, lean, and ready for your Vue.js dashboard or static site...

- **Embedded Service**

For scenarios where resources are constrained, SimpleW’s tiny memory footprint and single‑executable deployment model shine. It boots instantly at system startup, requires virtually no configuration files, and supports multiple architectures including x64 and ARM. This makes it ideal for lightweight background services, command‑and‑control agents, or any situation where simplicity and reliability are paramount.

- **Rapid API Prototype**

When you need to spin up a proof‑of‑concept API in record time, SimpleW excels. Its minimal configuration lets you define endpoints and data contracts in just a few lines of code. With JSON serialization handled automatically and no heavyweight server plumbing to configure, developers can focus on business logic from the very first compile. The result is a prototype that can be demoed to stakeholders within hours rather than days.


## Why this library ?

To my opinion, modern web application architecture should be based on a REST API which acts as a contract between 2 parts :
- backend (only one) : developer feels free to use/change the technology he wants (C#, Go, Rust, PHP...) but must provide and follow the REST API.
- frontend (one or many) : developer feels free to use/change the technology he wants (SPA/Vue, SPA/React, Mobile/Android...) but must consume and follow the REST API.

### My needs

**Frontend** :

- I prefer [SPA](https://en.wikipedia.org/wiki/Single-page_application) using [Vite](https://vitejs.dev/), [Vue](https://vuejs.org) and [Vuetify](https://vuetifyjs.com).

**Backend** :

- written in C#, the language i 😍.
- must be easy to integrate, lightweight with a minimal footprint.
- must support Routing, Websocket, SSE, CORS.
- don't need to have template engine as i write frontend in a separated project.
- must serve static files (static files are the result of my `npm run build` vite project)
- observality : trace each request and monitor performances


### The existings projects
- [ASP.NET Core](https://learn.microsoft.com/fr-fr/aspnet/core/?view=aspnetcore-8.0) :
    - too many features i don't need, i don't want _(Razor, Blazor...)_.
    - overcomplicated to customize some behaviour
    - too heavy, sometimes i have a very small API.
- [IIS](https://iis.net/) an old _« usine à gaz »_ on Windows, Kestrel and SignalR the same on Linux.
- [EmbedIO](https://github.com/unosquare/embedio) : long time v2 user, i dislike the rewrite of the v3. Moreover, it uses the old Microsoft `HttpListener` and the `websocket-sharp` alternative was not perfect.
- [GenHttp](https://genhttp.org) : feels promising but i was in the process of writting my own.
- __[NetCoreServer](https://github.com/chronoxor/NetCoreServer)__ : WHOA 😮 ! Fast, simple, extremly well design, extendable BUT no RESTAPI... Wait, what if i use the whole `OnReceivedRequest()` event to do exactly what i want 🤔

### This project

SimpleW is the result of adding features to the `OnReceivedRequest()` of [NetCoreServer](https://github.com/chronoxor/NetCoreServer).

After 3 years grade production, SimpleW serves many APIs without any issue, gains some cool features but still always lightweight and easy to integrate.

Feel free to report issue.
