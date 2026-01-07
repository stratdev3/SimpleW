# CORS

Modern web browsers (Chrome, Firefox, Safari, Edge…) **restrict JavaScript from calling APIs hosted on a different origin**.

This restriction is enforced by the browser, not by the server.

[CORS (Cross-Origin Resource Sharing)](https://developer.mozilla.org/fr/docs/Web/HTTP/CORS) defines how a server can explicitly allow cross-origin requests by returning specific HTTP headers.

Conceptually :

> CORS is a browser security contract. The server declares what is allowed; the browser enforces it.


## CORS in SimpleW

SimpleW provides a dedicated `CorsModule` to handle CORS concerns.

Key characteristics :
- Implemented as a single **middleware**
- Runs early in the request pipeline
- Handles both **preflight (OPTIONS)** and actual requests
- Centralized and explicit configuration

CORS logic is **not spread across handlers**.


## How It Works

To configure a CORS policy, use [`SimpleWServer.UseCorsModule()`](../reference/corsmodule.md) :

```csharp
// set CORS policy
server.UseCorsModule(options => {
    options.Prefix = "/api";
    options.AllowedOrigins = new[] { "http://localhost:2015" };
    options.AllowCredentials = true;
    options.AllowedMethods = "GET, POST, OPTIONS";
    options.MaxAgeSeconds = 600;
});
```

This configuration means:
- CORS applies only to routes under `/api`
- Only requests from `http://localhost:2015` are allowed
- Credentials (cookies, auth headers) are permitted
- Only `GET`, `POST`, and `OPTIONS` are accepted
- Preflight responses are cached by the browser for 10 minutes


## Preflight Requests (OPTIONS)

When required, browsers automatically issue an **OPTIONS preflight request**.

The CorsModule :
- Detects preflight requests
- Responds with the appropriate CORS headers
- Short-circuits the pipeline when possible

Handlers **do not need to handle OPTIONS explicitly**.
