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


## Why this library ?

Most .NET web stacks are powerful but complex. I wanted a **minimal, hackable server** that could be dropped into any app, or used as a base for custom frameworks, game servers, or embedded tools — while still delivering very good performances.

But there's more : dotnet environment suffers of a major issue. **There is only one "_professionally accepted_" web server. **ASP.NET Core** is the de facto standard and if you're not using it, you're considered a serious... amateur.

**That's a shame !** Not only does an ecosystem need alternatives to grow and improve, but no single product can cover 100% of its users’ needs. I'm certainly not claiming to replace ASP.NET Core or event compete with it, but I want to bring something different in an opinionated way. And I'm not the only one, other devs and organizations have done the same :

- [NetCoreServer](https://github.com/chronoxor/NetCoreServer) : still the State Of The Art in terms of performance and design !
- [Fast-Endpoints](https://fast-endpoints.com/) : built on top of ASP.NET Core, but with a cleaner and nicer API !
- [GenHTTP](https://github.com/Kaliumhexacyanoferrat/GenHTTP) : modular on its core, its author support many engines and contexts !
- [Wired.IO](https://mda2av.github.io/Wired.IO.Docs/) : aims to be the fastest, and it actually is. This guy delivers !
- [EmbedIO](https://github.com/unosquare/embedio) : no longer maintained, but it was one of the first. A true legacy !


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

No magic. No bloat. Just what is needed.

If you find a bug, have an idea, or a missing feature — **feel free to open an issue**.

SimpleW is opinionated, but open.
