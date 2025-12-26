# BasicAuthModule

The `BasicAuthModule` is used to setup Basic Authentication.
This module setup a middleware but even it is called multiples times to setup differents prefix, there will be only one iteration on the pipeline : the configuration of all prefixes and users is stored in a global registry shared between all `SimpleWServer` instances.


## Definition

```csharp
/// <summary>
/// Use Basic Auth Module (adds/updates a prefix rule; installs ONE middleware per server).
/// It setups a Middleware
/// </summary>
public static SimpleWServer UseBasicAuthModule(this SimpleWServer server, Action<BasicAuthOptions>? configure = null)
```

The options are the followings

```csharp
/// <summary>
/// Apply BasicAuth only for paths starting with this prefix (default "/")
/// </summary>
public string Prefix { get; set; } = "/";
```

```csharp
/// <summary>
/// Realm displayed by clients (default "Restricted")
/// </summary>
public string Realm { get; set; } = "Restricted";
```

```csharp
/// <summary>
/// If true, OPTIONS requests under Prefix bypass auth (default true)
/// This is useful for CORS preflight requests or API gateways
/// </summary>
public bool BypassOptionsRequests { get; set; } = true;
```

```csharp
/// <summary>
/// Users list (username/password). Ignored if CredentialValidator is set.
/// </summary>
public BasicUser[] Users { get; set; } = Array.Empty<BasicUser>();
```

```csharp
/// <summary>
/// Optional validator (return true to allow). Has priority over Users.
/// </summary>
public Func<string, string, bool>? CredentialValidator { get; set; }
```


## Example

```csharp:line-numbers
// setup cors
server.UseBasicAuthModule(options => {
    o.Prefix = "/api/admin";
    o.Realm = "Admin";
    o.Users = new[] { new BasicAuthModuleExtension.BasicAuthOptions.BasicUser("user", "password") };
});
```

See more [examples](../guide/basicauthmodule.md).