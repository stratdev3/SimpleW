using System.Net;
using SimpleW;
using SimpleW.Service.OpenID;


namespace example;

internal sealed class OpenIdScenario : IScenario {

    public string Name => "openid";

    public string Description => "OpenID Connect login, callback, logout and current-user routes.";

    public string Usage => " --authority uri --client-id value --redirect-uri uri [--client-secret value] [--provider name] [--scopes openid,profile,email]";

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("authority", "client-id", "client-secret", "provider", "redirect-uri", "scopes");

        Uri authority = RequireAbsoluteUri(arguments, "authority");
        string clientId = arguments.Require("client-id");
        string? clientSecret = arguments.Get("client-secret");
        string provider = arguments.Get("provider", "demo")!;
        Uri redirectUri = RequireAbsoluteUri(arguments, "redirect-uri");
        string[] scopes = arguments.Get("scopes", "openid,profile,email")!
                                   .Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        SimpleWServer server = ExampleServer.Create(arguments);
        server.UseOpenIDModule(options => {
            options.CookieSecure = string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            options.Add(provider, providerOptions => {
                providerOptions.Authority = authority.AbsoluteUri;
                providerOptions.ClientId = clientId;
                providerOptions.ClientSecret = clientSecret;
                providerOptions.RedirectUri = redirectUri.AbsoluteUri;
                providerOptions.Scopes = scopes;
            });
        });

        string encodedProvider = Uri.EscapeDataString(provider);
        string loginUrl = $"/auth/oidc/login/{encodedProvider}?returnUrl=/api/me";
        server.MapGet("/", (HttpSession session) => {
            string html = $"""
                <!doctype html>
                <html lang="en">
                <head><meta charset="utf-8"><title>SimpleW OpenID</title></head>
                <body>
                  <h1>OpenID Connect</h1>
                  <p>Provider: {WebUtility.HtmlEncode(provider)}</p>
                  <p><a href="{loginUrl}">Sign in</a></p>
                  <p><a href="/api/me">Current user</a></p>
                  <p><a href="/auth/oidc/logout?returnUrl=/">Sign out</a></p>
                </body>
                </html>
                """;
            return session.Response.Html(html);
        });
        server.MapGet("/api/me", object (HttpSession session) => {
            HttpPrincipal principal = session.Principal;
            if (!principal.IsAuthenticated) {
                return session.Response.Unauthorized("Not authenticated.");
            }

            return new {
                authenticated = true,
                name = principal.Name,
                email = principal.Email,
                provider = principal.Get("provider")
            };
        });

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

    private static Uri RequireAbsoluteUri(ExampleArguments arguments, string name) {
        _ = arguments.Require(name);
        return arguments.GetAbsoluteUri(name)!;
    }

}
