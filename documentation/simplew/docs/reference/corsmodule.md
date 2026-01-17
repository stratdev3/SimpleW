# CorsModule

The `CorsModule` is used to setup Cross-Origin Resource Sharing policy.
This module setup a middleware.


## Definition

```csharp
/// <summary>
/// Use CORS Module
/// It setups a Middleware
/// </summary>
public static SimpleWServer UseCorsModule(this SimpleWServer server, Action<CorsOptions>? configure = null)
```

The options are the followings

```csharp
/// <summary>
/// Apply CORS only for paths starting with this prefix (default "/")
/// </summary>
public string Prefix { get; set; } = "/";
```

```csharp
/// <summary>
/// Allow all origins ("*").
/// Note: if AllowCredentials is true, "*" cannot be used => origin is echoed instead.
/// </summary>
public bool AllowAnyOrigin { get; set; } = false;
```

```csharp
/// <summary>
/// Allowed origins list (exact match, case-insensitive)
/// </summary>
public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
```

```csharp
/// <summary>
/// Optional custom origin validator (return true to allow).
/// If set, it has priority over AllowedOrigins.
/// </summary>
public Func<string, bool>? OriginValidator { get; set; }
```

```csharp
/// <summary>
/// Allow cookies/Authorization header to be sent by browser.
/// </summary>
public bool AllowCredentials { get; set; } = false;
```

```csharp
/// <summary>
/// Allowed methods for preflight response.
/// If empty, the module will echo the Access-Control-Request-Method (when present),
/// otherwise default to common methods.
/// </summary>
public string? AllowedMethods { get; set; } = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
```

```csharp
/// <summary>
/// Allowed headers for preflight response.
/// If empty, the module will echo Access-Control-Request-Headers (when present).
/// </summary>
public string? AllowedHeaders { get; set; }
```

```csharp
/// <summary>
/// Response headers accessible from JS (non-simple headers)
/// </summary>
public string? ExposedHeaders { get; set; }
```

```csharp
/// <summary>
/// Preflight cache duration (Access-Control-Max-Age)
/// </summary>
public int? MaxAgeSeconds { get; set; }
```

## Example

```csharp:line-numbers
server.UseCorsModule(options => {
    o.Prefix = "/api";
    o.AllowedOrigins = new[] { "http://localhost:2015" };
    o.AllowCredentials = true;
    o.AllowedMethods = "GET, POST, OPTIONS";
    o.MaxAgeSeconds = 600;
});
```

See more [examples](../guide/cors.md).