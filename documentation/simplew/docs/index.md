---
# https://vitepress.dev/reference/default-theme-home-page
layout: home

hero:
    name: "SimpleW"
    text: "Lightweight Web Server <br />for .NET"
    tagline: Simple by design. Standalone or embedded.
    actions:
        - theme: brand
          text: Get Started
          link: /guide/getting-started
        - theme: alt
          text: What is SimpleW?
          link: /guide/what-is-simplew
        - theme: alt
          text: GitHub
          link: https://github.com/stratdev3/SimpleW

features:
    - icon: >-
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="m7.5 4.27 9 5.15"></path><path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z"></path><path d="m3.3 7 8.7 5 8.7-5"></path><path d="M12 22V12"></path></svg>
      title: Standalone or Embedded
      details: Run a dedicated web service or add HTTP endpoints directly to an existing .NET application.
    - icon: >-
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"></polygon></svg>
      title: Zero-Dependency Core
      details: Native sockets, a custom HTTP parser, and no ASP.NET dependency between your code and the connection.
    - icon: >-
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.68 0C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1Z"></path><path d="m9 12 2 2 4-4"></path></svg>
      title: Production Proven
      details: Five years in production, powering real APIs and traffic while remaining explicit and predictable.
    - icon: >-
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M15.39 4.39a1 1 0 0 0 1.68-.474 2.5 2.5 0 1 1 3.014 3.015 1 1 0 0 0-.474 1.68l1.683 1.682a2.414 2.414 0 0 1 0 3.414L19.61 15.39a1 1 0 0 1-1.68-.474 2.5 2.5 0 1 0-3.014 3.015 1 1 0 0 1 .474 1.68l-1.683 1.682a2.414 2.414 0 0 1-3.414 0L8.61 19.61a1 1 0 0 0-1.68.474 2.5 2.5 0 1 1-3.014-3.015 1 1 0 0 0 .474-1.68l-1.683-1.682a2.414 2.414 0 0 1 0-3.414L4.39 8.61a1 1 0 0 1 1.68.474 2.5 2.5 0 1 0 3.014-3.015 1 1 0 0 1-.474-1.68l1.683-1.682a2.414 2.414 0 0 1 3.414 0z"></path></svg>
      title: Addons, Not Bloat
      details: Add authentication, firewall, automatic TLS, background jobs, or another engine only when you need them.

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

// run server
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
            options.Enabled = true;
            options.IncludeStackTrace = true;
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
            options.AllowCountries.Add(CountryRule.Any("FR"));
        });

        // OpenID
        server.UseOpenIDModule(options => {
            options.Add("google", o => {
                o.Authority = "https://accounts.google.com";
                o.ClientId = "<google-client-id>";
                o.ClientSecret = "<google-client-secret>";
                o.RedirectUri = "https://myapp.example.com/auth/oidc/callback/google";
            });
        });

        // run server
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

:::