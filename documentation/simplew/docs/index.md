---
# https://vitepress.dev/reference/default-theme-home-page
layout: home

hero:
    name: "SimpleW"
    text: "Modern Web Server <br />for .NET"
    tagline: Designed for Simplicity. Built for Speed.
    #tagline: Designed for Simplicity. Built for Speed. Packed with Power.
    #tagline: Powerfully Simple. Seriously Fast.
    #tagline: Simple by Design. Fast and Powerful.
    #tagline: Simple. Fast. Powerful.
    #tagline: Simplicity First. Performance Built-In.
    #tagline: Simple by Design. Built for Speed.
    actions:
        - theme: brand
          text: What is SimpleW?
          link: /guide/what-is-simplew
        - theme: alt
          text: Quick Start
          link: /guide/getting-started
        - theme: alt
          text: GitHub
          link: https://github.com/stratdev3/SimpleW

features:
    - icon: ⚡
      title: Zero Dependencies, Maximum Speed
      details: Built on top of native sockets. Minimal overhead, instant startup and high-performance workloads.
    - icon: 🌐
      title: Flexible Web Server
      details: Handle APIs, static files, and dynamic content through a simple routing model — with full control over behavior.
    - icon: 🛡️
      title: Production-Grade Core
      details: A fully integrated core designed for real-world usage covering security, communication, and observability without external dependencies.
    - icon: 🧩
      title: Powerful Addons
      details: Designed for extensibility. Integrate additional capabilities through independent modules without bloating your core.

---

## From Zero to Production

Start with a few lines of code, then add controllers, addons, and hosting features as your application grows.

::: code-group

```csharp:line-numbers [Minimal]
// debug log
Log.SetSink(Log.ConsoleWriteLine, LogLevel.Debug);

// listen to all IPs port 8080
var server = new SimpleWServer(IPAddress.Any, 8080);

// map handler to a route
server.MapGet("/api/test/hello", () => {
    return new { message = "Hello World !" };
});

// start a blocking background server
await server.RunAsync();
```

```csharp:line-numbers [Controllers]
class Program {
    static async Task Main() {
        // server
        await new SimpleWServer(IPAddress.Any, 8080)
                    .MapControllers<Controller>("/api")
                    .RunAsync();
    }
}

public class TestController : Controller {
    // handler
    [Route("GET", "/test")]
    public object Hello(string name) {
        return new { message = $"{name}, Hello World !" };
    }
}
```

```csharp:line-numbers [Production]
class Program {

    static async Task Main() {

        var server = new SimpleWServer(IPAddress.Any, 8080);

        // telemetry
        server.ConfigureTelemetry(options => {
            options.IncludeStackTrace = true;
            options.EnrichWithHttpSession = (activity, session) => {
                // override host with the X-Forwarded-Host header (set by a trusted reverse proxy)
                if (session.Request.Headers.TryGetValue("X-Forwarded-Host", out string? host) {
                    activity.SetTag("url.host", host);
                }
            };
        });

        // socket tuning
        server.Configure(options => {
            options.TcpNoDelay = true;
            options.ReuseAddress = true;
            options.TcpKeepAlive = true;
        });

        // find all classes based on Controller class, and serve on the "/api" endpoint
        server.MapControllers<Controller>("/api");

        // static files with cache
        server.UseStaticFilesModule(options => {
            options.Path = "/app/www/public";
            options.CacheTimeout = TimeSpan.FromDays(1);
        });

        // Firewall
        server.UseFirewallModule(options => {
            options.AllowRules.Add(IpRule.Cidr("10.0.0.0/8"));
            options.AllowRules.Add(IpRule.Single("127.0.0.1"));
            options.MaxMindCountryDbPath = "/app/data/GeoLite2-Country.mmdb";
            options.AllowCountries.Add(CountryRule.Any("US"));
        });

        // OpenID
        server.UseOpenIDModule(options => {
            options.Add("google", o => {
                o.Authority = "https://accounts.google.com";
                o.ClientId = "<google-client-id>";
                o.ClientSecret = "<google-client-secret>";
                o.PublicBaseUrl = "https://myapp.example.com";
            });
        });

        // start a blocking background server
        await server.RunAsync();
    }
}

public class TestController : Controller {
    // handler
    [Route("GET", "/test")]
    public object Hello(string name) {
        return new { message = $"{name}, Hello World !" };
    }
}
```

```csharp:line-numbers [ASP.NET-like]
var builder = SimpleWHost.CreateApplicationBuilder(args)
                         .UseMicrosoftLogging();

builder.ConfigureSimpleW(server => {
    configureApp: server => {
        // razor
        server.UseRazorModule(options => {
            options.ViewsPath = "Views";
        });
        // OpenAPI JSON
        server.MapGet("/swagger.json", static (HttpSession session) => {
            return Swagger.Json(session);
        });
        // Swagger UI
        server.MapGet("/swagger", static (HttpSession session) => {
            return Swagger.UI(session);
        });
        // routes
        server.MapGet("/hello", () => {
            return new { mesage = "Hello World !" };
        });
        // ssl
        X509Certificate2 cert = new(@"C:\Users\SimpleW\ssl\domain.pfx", "password");
        var sslcontext = new SslContext(SslProtocols.Tls12 | SslProtocols.Tls13, cert, clientCertificateRequired: false, checkCertificateRevocation: false);
        server.UseHttps(sslcontext);
    },
    configureServer: options => {
        options.TcpNoDelay = true;
        options.ReuseAddress = true;
        options.TcpKeepAlive = true;
    }
});

var host = builder.Build();
await host.RunAsync();
```

:::