---
# https://vitepress.dev/reference/default-theme-home-page
layout: home

hero:
    name: "SimpleW"
    text: "Web server Library .NET Core"
    #tagline: Simple, Fast and Featured
    #tagline: Designed for Simplicity. Built for Speed. Packed with Power.
    tagline: Powerfully Simple. Blazingly Fast.
    image:
        src: /logo.png
        alt: SimpleW
    actions:
        - theme: brand
          text: What is SimpleW?
          link: /what-is-simplew
        - theme: alt
          text: Quick Start
          link: /getting-started
        - theme: alt
          text: GitHub
          link: https://github.com/stratdev3/SimpleW

features:
    - icon: 🚀
      title: Ultra-light & Blazing Fast
      details: Built on top of native sockets using NetCoreServer. Minimal overhead, instant startup, ideal for microservices, embedded apps, and high-performance workloads.
    - icon: 🧩
      title: API REST
      details: Define routes and controllers without ceremony. Automatic JSON serialization. Focus on your business logic — no boilerplate, no mess.
    #- icon: 🔑
    #  title: Json Web Token
    #  details: Add token-based auth and real-time communication in minutes. Secure and scalable, without pulling in heavy frameworks.
    - icon: 🔐
      #icon: ⇄
      title: Built-in JWT & WebSocket
      details: Add token-based auth and real-time communication in minutes. Secure and scalable, without pulling in heavy frameworks.
    - icon: 🔬
      title: Observability
      details: Integrated OpenTelemetry with the support of distributed tracing and metrics collection. Easier to monitor, debug, and analyze requests.
---
