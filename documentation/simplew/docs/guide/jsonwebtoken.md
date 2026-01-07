# JWT Authentication


[JSON Web Tokens](https://jwt.io/) are an open, industry standard [RFC 7519](https://tools.ietf.org/html/rfc7519) method for representing claims securely between two parties.

In SimpleW you can work with JWT at three levels :
- Highest-level : if your app is built around SimpleW’s request pipeline and authentication conventions
- Mid-level : handle the payload yourself
- Low-level : you manage the encoding/decoding yourself


## Highest-level

If your app is built around SimpleW’s request pipeline and authentication conventions, you can use the **highest-level** API with ``Request.User``.

### Example

```csharp:line-numbers
class Program {
    static async Task Main() {

        var server = new SimpleWServer(IPAddress.Any, 2015);
        server.Configure(options => {
            // configure with "Azerty0123456789!" as password
            options.JwtOptions = new JwtOptions("Azerty0123456789!");
        });

        // forge a jwt
        server.MapGet("/api/jwt/forge", (HttpSession session) => {
            var payload = new WebUser() {
                Identity = true,
                Id = Guid.NewGuid(),
                FullName = "Chris",
                Login = "chris",
                Mail = "me@simplew.net",
                Profile = "Administrator",
                Roles = [ "admin" ]
            };
            return session.CreateJwt(JwtTokenPayload.Create(TimeSpan.FromMinutes(15)), payload.ToDict());
        });

        // access a secured endpoint
        server.MapGet("/api/test/hello", object (HttpSession session) => {
            // need administrator role !
            if (!session.Request.User.IsInRoles("admin")) {
                return session.Response.Unauthorized();
            }
            return new { message = $"{session.Request.User.FullName}, Hello World !" };
        });

        Console.WriteLine("1. get a jwt : http://localhost:2015/api/jwt/forge");
        Console.WriteLine("2. access secure endpoint : http://localhost:2015/api/test/hello");
        await server.RunAsync();

    }
}
```

- Open (1) to get a bearer token containing a [`IWebUser`](../reference/iwebuser.md) as a payload, forged by [`SimpleWExtension.CreateJwt()`](../reference/simplewextension.md#createjwt)
- Open (2) to access a secured endpoint (you have to pass the previous bearer token as header `Authorization` or as a query string `jwt` (see [Jwt](../reference/httprequest.md#jwt) for more informations)

::: tip Info
This level is recommended when most of your endpoints rely on authentication and you want consistent behavior everywhere with minimal boilerplate.
:::


## Mid-level

You want to use own payload but still using the underlying infrastructure SimpleW offer.
This is exactly the purpore of `Request.JwtToken` and `Request.JwtError`.

### Example

```csharp:line-numbers
class Program {

    static async Task Main() {

        var server = new SimpleWServer(IPAddress.Any, 2015);
        server.Configure(options => {
            // configure with "Azerty0123456789!" as password
            options.JwtOptions = new JwtOptions("Azerty0123456789!");
        });

        // forge a jwt
        server.MapGet("/api/jwt/forge", (HttpSession session) => {
            var payload = new CustomPayload() {
                Name = "Chris"
                Profile = "Administrator"
            };
            return session.CreateJwt(JwtTokenPayload.Create(TimeSpan.FromMinutes(15)), payload.ToDict());
        });

        // access a secured endpoint
        server.MapGet("/api/test/hello", object (HttpSession session) => {
            // check for a valid jwt
            if (session.Request.JwtError == null || session.Request.JwtError != JwtError.None) {
                return session.Response.Unauthorized(); 
            }
            // get the string serialized payload
            var serializedPayload = session.Request.JwtToken.RawPayload;
            // deserialized into your custom format
            var payload = session.Request.JsonEngine.Deserialize<CustomPayload>(serializedPayload);

            if (payload?.Profil != "Administrator") {
                return session.Response.Unauthorized();
            }
            return new { message = $"{payload.Name}, Hello World !" };
        });

        Console.WriteLine("1. get a jwt : http://localhost:2015/api/jwt/forge");
        Console.WriteLine("2. access secure endpoint : http://localhost:2015/api/test/hello");
        await server.RunAsync();
    }

    class CustomPayload {
        public string Name { get; set; }
        public string Profile { get; set; }
    }
}
```

- Open (1) to get a bearer token containing your `CustomPayload`, forged by [`SimpleWExtension.CreateJwt()`](../reference/simplewextension.md#createjwt)
- Open (2) to access a secured endpoint (you have to pass the previous bearer token as header `Authorization` or as a query string `jwt` (see [Jwt](../reference/httprequest.md#jwt) for more informations)

::: tip Info
This level is recommended is you have a custom payload format.
:::


## Low-level

### Encode

Use `Jwt.EncodeHs256()` when you want to fully control how tokens are forged (secret management, external usage, tooling, etc.).

```csharp
var standard = JwtTokenPayload.Create(TimeSpan.FromMinutes(15), issuer: "simplew-sample");

var payload = new Dictionary<string, object?>() {
     { "id", Guid.NewGuid() },
     { "name", "John Doe" },
     { "roles", new[] { "account", "infos" } }
};

// HS256 token string (HMAC-SHA256)
var token = Jwt.EncodeHs256(Session.JsonEngine, standard, payload, secret: "Azerty0123456789!");
```

Notes :
- `JwtTokenPayload` is for registered claims (`exp`, `nbf`, `iat`, `iss`, `sub`, `aud`).
- `payload` is for custom claims.
- If a custom claim key conflicts with a registered claim name, encoding throws.

### Decode and Validate

`Jwt.TryDecodeAndValidate()` verifies :
- token structure and JSON parsing
- algorithm is `HS256`
- signature is valid
- optionally validates `exp`, `nbf`, and `iss` depending on `JwtOptions`

```csharp
var options = new JwtOptions("secret") {
    ValidateExp = true,
    ValidateNbf = true,
    // ValidateIss = true,
    // ValidIss = "simplew-sample",
};

if (!Jwt.TryDecodeAndValidate(Session.JsonEngine, token, options, out var jwt, out var err)) {
    Console.WriteLine($"JWT rejected: {err}");
    return;
}

// jwt.RawPayload contains the payload JSON string
var user = Session.JsonEngine.Deserialize<UserToken>(jwt!.RawPayload);
```