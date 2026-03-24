# Jwt

The [`SimpleW.Helper.Jwt`](https://www.nuget.org/packages/SimpleW.Helper.Jwt) package provides a **lightweight JWT Bearer helper** fully integrated with the SimpleW identity system.


## Features

It allows you to :

- Create JWT tokens from `HttpIdentity` or `HttpPrincipal`
- Validate tokens and automatically rebuild a `HttpPrincipal`
- Work seamlessly with `Session.Principal` and `Controller.Principal`
- Supports **HS256 / HS384 / HS512**
- Built-in validation : signature, expiration (`exp`), not before (`nbf`), issuer (`iss`), audience (`aud`)


## Requirements

- .NET 8.0
- SimpleW (core server)


## Installation

Install the package from NuGet:

```sh
$ dotnet add package SimpleW.Helper.Jwt --version 26.0.0-rc.20260326-1604
```


### Creating a Token

A token is created from an identity.

```csharp
var options = JwtBearerOptions.Create("secret");

var identity = new HttpIdentity(
    isAuthenticated: true,
    authenticationType: "Custom",
    identifier: "user-1",
    name: "John",
    email: "john@doe.com",
    roles: new[] { "admin" },
    properties: null
);

string token = JwtBearerHelper.CreateToken(
    options,
    identity,
    lifetime: TimeSpan.FromHours(1)
);
```


## Validating a Token

```csharp
if (JwtBearerHelper.TryValidateToken(options, token, out var principal, out var error))
{
    // valid token
}
else
{
    // invalid token
}
```

On success :
- A full `HttpPrincipal` is created
- Identity is marked as authenticated (`Bearer`)

```csharp
principal.Identity.IsAuthenticated == true
```


## Minimal Example

Using with SimpleW, there is two approaches to resolve the principal at request time.

::: code-group

```csharp [PrincipalResolver Approach]
server.ConfigurePrincipalResolver(session => {

    if (!session.Request.Headers.TryGetValue("Authorization", out var auth) || !auth.StartsWith("Bearer ")) {
        return HttpPrincipal.Anonymous;
    }

    string token = auth.Substring("Bearer ".Length);

    if (JwtBearerHelper.TryValidateToken(options, token, out var principal, out _)) {
        return principal;
    }

    return HttpPrincipal.Anonymous;
});
```

```csharp [Middleware Approach]
server.UseMiddleware(async (session, next) => {
    if (session.Request.Headers.TryGetValue("Authorization", out var auth)
        && auth.StartsWith("Bearer ")
    ) {
        string token = auth.Substring("Bearer ".Length);

        if (JwtBearerHelper.TryValidateToken(options, token, out var principal, out _)) {
            session.Principal = principal;
        }
    }

    await next();
});
```

:::


Once resolved, the principal is available everywhere :

::: code-group

```csharp [From Session]
server.MapGet("/api/test", (HttpSession session) => {
    if (!Session.Principal.IsAuthenticated) {
        return Response.Unauthorized();
    }

    return new {
        user = Session.Principal.Name
    };
});
```

```csharp [From Controller]
[Route("GET", "/user/principal")]
public object Get() {
    if (!Principal.IsAuthenticated) {
        return Response.Unauthorized();
    }

    return new {
        user = Principal.Name
    };
}
```

:::


## Integration Summary

| Step             | Responsibility         |
| ---------------- | ---------------------- |
| Create token     | your login/auth logic  |
| Validate token   | middleware or resolver |
| Assign Principal | SimpleW session        |
| Use Principal    | controllers / handlers |


## Security Notes

- Only **HMAC (symmetric)** algorithms are supported
- Tokens are **signed, not encrypted**
- Keep your secret key secure
- Always validate tokens before trusting them
