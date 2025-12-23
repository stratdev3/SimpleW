# StaticFilesModule


The `StaticFilesModule` serve static files and expose some options to adjust its behaviour and performances.


## Definition

```csharp
/// <summary>
/// Use Static Files Module
/// </summary>
/// <param name="server"></param>
/// <param name="configure"></param>
/// <returns></returns>
/// <exception cref="ArgumentException"></exception>
public static SimpleWServer UseStaticFilesModule(this SimpleWServer server, Action<StaticFilesOptions>? configure = null)
```

The options are the followings

```csharp
/// <summary>
/// Path of the directory to Server
/// </summary>
public string Path { get; set; }
```

```csharp
/// <summary>
/// Url Prefix to call this module
/// </summary>
public string Prefix { get; set; } = "/";
```

```csharp
/// <summary>
/// Cache file filter
/// </summary>
public string CacheFilter { get; set; } = "*";
```

```csharp
/// <summary>
/// Cache timeout
/// </summary>
public TimeSpan? CacheTimeout { get; set; }
```

```csharp
/// <summary>
/// If true, serves a minimal directory listing when no default document exists.
/// </summary>
public bool AutoIndex { get; set; } = false;
```

```csharp
/// <summary>
/// Default document for directory requests
/// </summary>
public string DefaultDocument { get; set; } = "index.html";
```

## Example

```csharp:line-numbers
// serve statics files
server.UseStaticFilesModule(options => {
    options.Path = @"C:\www\";                      // serve your files located here
    options.Prefix = "/";                           // to "/" endpoint
    options.CacheFilter = "*.csv";                  // cache only csv files
    options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
});
```

See more [examples](../guide/static-files.md).