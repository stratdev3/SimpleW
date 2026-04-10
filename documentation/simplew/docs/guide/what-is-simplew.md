# What is SimpleW?

SimpleW is a modern web server for .NET.<br />
It is designed for building fast and secure web applications.

<div class="tip custom-block" style="padding-top: 8px">

Just want to try it out? Skip to the [Quickstart](./getting-started).

</div>


## Architecture

SimpleW’s architecture and the motivations behind its core design choices :

- **Pure C# (100% managed code)**, running on .NET 8 or later.
- **Built on top of native sockets**, low latency.
- **Compiled delegate**, close to hard-coded method calls.
- **Custom parser**, low memory.
- **Cross‑platform support**, Windows/Linux/Android/MacOS.
- **NuGet package available**, **no dependency**, easy to integrate


## Philosophy

I want to keep the core simple and lightweight so that :
- The code can be reviewed by a developer in a few hours
- Or fully ingested (and reasoned about) by an AI in a few seconds

Addons are the place for everything that does not belong in the core.
Also, a product is much easier to adopt when there is a healthy ecosystem around it.


## Why this library ?

Most .NET web stacks are powerful but complex. I wanted a **minimal, hackable server** that could be dropped into any app, or used as a base for custom frameworks, game servers, or embedded tools — while still delivering very good performances.

But there is more: the .NET environment suffers from a major issue :
> **There is only one _professionally accepted_ web server**.
> ASP.NET Core is the de facto standard and if you're not using it, you're considered a serious... amateur.

**That's a shame !** Not only does an ecosystem need alternatives to grow and improve, but no single product can cover 100% of its users’ needs. I'm certainly not claiming to replace ASP.NET Core or event compete with it, but I want to bring something different in an opinionated way. And I'm not the only one, other devs have done the same :

- [NetCoreServer](https://github.com/chronoxor/NetCoreServer): still the state of the art in terms of performance and design!
- [Fast-Endpoints](https://fast-endpoints.com/): built on top of ASP.NET Core, but with a cleaner and nicer API!
- [GenHTTP](https://github.com/Kaliumhexacyanoferrat/GenHTTP): modular at its core, its author supports many engines and contexts!
- [Wired.IO](https://github.com/MDA2AV/Wired.IO): aims to be the fastest, and it actually is. This one delivers!
- [EmbedIO](https://github.com/unosquare/embedio): no longer maintained, but it was one of the first. A true legacy!


### This project

SimpleW is the result of years of experimentation, production usage, and frustration with existing solutions.
It is a web server designed to be lightweight, explicit, fast and easy to embed.

After 5 years in production, SimpleW:
- Powers multiple APIs
- Handles real-world traffic reliably
- Continues to gain useful features
- Remains simple and predictable


## Final Words

If you find a bug, have an idea, or a missing feature — **feel free to open an issue**.

SimpleW is opinionated, but open.
