# StaticFilesModule


The `StaticFilesModule` serves static files and exposes options to adjust its behaviour and performance.


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

The options are the following

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
/// If true, compressed gzip/Brotli variants are generated next to the source file and streamed from disk.
/// ".br" or ".gz" is appended to the original file name.
/// </summary>
public bool CompressedDiskCache { get; set; } = false;
```

```csharp
/// <summary>
/// Optional authorization callback. Return false to reject the request before serving a file.
/// </summary>
public Func<HttpSession, bool>? Authorize { get; set; }
```

```csharp
/// <summary>
/// Maximum size (in bytes) of a single file allowed to be stored in memory cache.
/// Null means unlimited. (default: 4 MiB)
/// </summary>
public long? MaxCachedFileBytes { get; set; } = 4 * 1024 * 1024;
```

```csharp
/// <summary>
/// Maximum total size (in bytes) of the in-memory file cache.
/// Null means unlimited. (default: 256 MiB)
/// </summary>
public long? MaxCacheTotalBytes { get; set; } = 256 * 1024 * 1024;
```

```csharp
/// <summary>
/// Maximum number of entries in the in-memory file cache.
/// Null means unlimited. (default: 10,000)
/// </summary>
public int? MaxCacheEntries { get; set; } = 10_000;
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

```csharp
// serve statics files
server.UseStaticFilesModule(options => {
    options.Path = @"C:\www\";                      // serve your files located here
    options.Prefix = "/";                           // to "/" endpoint
    options.CacheFilter = "*.csv";                  // cache only csv files
    options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
    options.MaxCachedFileBytes = 4 * 1024 * 1024;   // at most 4 MiB per cached file
    options.MaxCacheTotalBytes = 256 * 1024 * 1024; // at most 256 MiB in memory
    options.MaxCacheEntries = 10_000;                // at most 10,000 cached files
});
```

See more [examples](../guide/staticfiles.md).
