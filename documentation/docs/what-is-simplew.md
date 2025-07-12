# What is SimpleW?

SimpleW is a .NET Core library designed for building fast and secure web applications.
It is based in the the great [NetCoreServer](https://github.com/chronoxor/NetCoreServer) socket server.

<div class="tip custom-block" style="padding-top: 8px">

Just want to try it out? Skip to the [Quickstart](./getting-started).

</div>


## Architecture

SimpleW is lightweight, easy to integrate, fast, with a minimal footprint :
- written in pure C# 100% managed code
- NET7
- crossplateform (windows/linux/macos)
- only one dependancy, `Newtonsoft.Json` for serialization/deserialization

Note : `Reflection` is only used to list routes, __once__, before server start.
An `expression tree` is built to call method fast __without__ using any slow `T.GetMethod().Invoke()`.


## Use Cases

- **static site**

SimpleW can server static files in a ....

- **api**


## Why i wrote this library

To my opinion, modern web application architecture should be based on a REST API which acts as a contract between 2 parts :
- backend (only one) : developer feels free to use/change the technology he wants (C#, Go, Rust, PHP...) but must provide and follow the REST API.
- frontend (one or many) : developer feels free to use/change the technology he wants (SPA/Vue, SPA/React, Mobile/Android...) but must consume and follow the REST API.

### So, my needs

#### Frontend

I prefer [SPA](https://en.wikipedia.org/wiki/Single-page_application) using [Vite](https://vitejs.dev/), [Vue](https://vuejs.org) and [Vuetify](https://vuetifyjs.com).

#### Backend

- written in C#, the language i 😍.
- must be easy to integrate, lightweight with a minimal footprint.
- must support Routing, Websocket, CORS.
- don't need to have template engine as i write frontend in a separated project.
- must serve static files (static files are the result of my `npm run build` vite project)


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

SimpleW is the result of adding basic RESTAPI features to the `OnReceivedRequest()` of [NetCoreServer](https://github.com/chronoxor/NetCoreServer).

After 3 years grade production, SimpleW serves many APIs without any issue, gains some cool features but still always lightweight and easy to integrate.

Feel free to report issue.
