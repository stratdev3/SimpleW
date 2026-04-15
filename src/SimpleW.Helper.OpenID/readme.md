# SimpleW.Helper.OpenID

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![NuGet Package](https://img.shields.io/nuget/v/SimpleW.Helper.OpenID)](https://www.nuget.org/packages/SimpleW.Helper.OpenID)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW.Helper.OpenID)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

### Features

`SimpleW.Helper.OpenID` is the reusable OpenID Connect auth engine for SimpleW.

It lets you:
- authenticate a request from a provider-signed `id_token` auth cookie
- create a provider challenge URL
- complete the callback flow
- keep authenticated users logged in across server restarts while the `id_token` is valid
- sign users out cleanly

This package does not decide which route must be protected. That policy stays in your middleware.
It does not store user sessions in server memory: every `TryAuthenticate()` reads the auth cookie,
validates the OpenID `id_token`, and rebuilds the `HttpPrincipal`.

### Getting Started

Minimal helper usage with controller metadata:

```cs
using System.Net;
using SimpleW;
using SimpleW.Helper.OpenID;

namespace Sample {

    internal class Program {

        public static OpenIDHelper Oidc { get; private set; } = default!;

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 8080);

            Oidc = new OpenIDHelper(options => {
                options.CookieSecure = false; // local HTTP development only

                options.Add("google", provider => {
                    provider.Authority = "https://accounts.google.com";
                    provider.ClientId = "<google-client-id>";
                    provider.ClientSecret = "<google-client-secret>";
                    provider.RedirectUri = "http://127.0.0.1:8080/auth/openid/callback/google";
                });
            });

            server.UseMiddleware(async (session, next) => {
                if (Oidc.TryAuthenticate(session, out HttpPrincipal principal)) {
                    session.Principal = principal;
                }

                if (session.Metadata.Has<AllowAnonymousAttribute>()) {
                    await next().ConfigureAwait(false);
                    return;
                }

                OpenIDAuthAttribute? auth = session.Metadata.Get<OpenIDAuthAttribute>();
                if (auth != null && !session.Principal.IsAuthenticated) {
                    string url = await Oidc.CreateChallengeUrlAsync(
                        session,
                        auth.Provider,
                        returnUrl: session.Request.RawTarget
                    ).ConfigureAwait(false);

                    await session.Response.Redirect(url).SendAsync().ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });

            server.MapController<OpenIDController>("/auth");
            server.MapController<AccountController>("/api");

            await server.RunAsync();
        }

    }

    [Route("/openid")]
    public class OpenIDController : Controller {

        [AllowAnonymous]
        [Route("GET", "/login/:provider")]
        public async ValueTask<object> Login(string provider) {
            string url = await Program.Oidc.CreateChallengeUrlAsync(Session, provider).ConfigureAwait(false);
            return Response.Redirect(url);
        }

        [AllowAnonymous]
        [Route("GET", "/callback/:provider")]
        public async ValueTask<object> Callback(string provider) {
            OpenIDCallbackResult result = await Program.Oidc.CompleteCallbackAsync(Session, provider).ConfigureAwait(false);

            if (!result.IsSuccess) {
                return Response.Status(result.StatusCode).Json(new { ok = false, error = result.Error });
            }

            Session.Principal = result.Principal!;
            return Response.Redirect(result.ReturnUrl);
        }

        [AllowAnonymous]
        [Route("GET", "/logout")]
        public object Logout() {
            string returnUrl = Program.Oidc.SignOut(Session, "/");
            return Response.Redirect(returnUrl);
        }

    }

    [Route("/account")]
    public class AccountController : Controller {

        [AllowAnonymous]
        [Route("GET", "/public")]
        public object Public() {
            return new {
                login = "/auth/openid/login/google?returnUrl=/api/account/me",
                logout = "/auth/openid/logout?returnUrl=/"
            };
        }

        [OpenIDAuth("google")]
        [Route("GET", "/me")]
        public object Me() {
            return new {
                user = Principal.Name,
                email = Principal.Email,
                provider = Principal.Get("provider")
            };
        }

    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class OpenIDAuthAttribute : Attribute, IHandlerMetadata {

        public OpenIDAuthAttribute(string provider) {
            Provider = provider;
        }

        public string Provider { get; }

    }
}
```

If you want a ready-to-use module with automatic authentication and technical routes already wired, use `SimpleW.Service.OpenID`.

## Documentation

To check out docs, visit [simplew.net](https://simplew.net).

## Changelog

Detailed changes for each release are documented in the [CHANGELOG](https://github.com/stratdev3/SimpleW/blob/master/release.md).

## Contribution

Feel free to report issue.

## License
This library is under the MIT License.
