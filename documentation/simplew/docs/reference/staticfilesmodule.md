# StaticFilesModule


The `StaticFilesModule` 

```csharp
/// <summary>
/// Add static content
/// The timeout parameter control how long the content is cached (null or 0 mean no cache at all)
/// When cache, there is an underlying file watcher to refresh cache on file change
/// </summary>
/// <param name="path">Static content path</param>
/// <param name="prefix">Cache prefix (default is "/")</param>
/// <param name="filter">Cache filter (default is "*.*")</param>
/// <param name="timeout">Refresh cache timeout (0 or null mean no cache, default: null)</param>
void AddStaticContent(string path, string prefix = "/", string filter = "*.*", TimeSpan? timeout = null)
```

This method expose all files in `path` in the `Router` and served by the web server under the `prefix` endpoint.
There is a `filter` and a `timeout` to control the cache lifetime (default: 1 hour).


```csharp
/// <summary>
/// File to get by default (default: "index.html")
/// </summary>
string DefaultDocument { get; set; } = "index.html";
```

This property change the default file in of a static content of no file has been selected.

```csharp
/// <summary>
/// Enable AutoIndex when DefaultDocument does not exists
/// scope : global to all AddStaticContent()
/// </summary>
bool AutoIndex { get; set; } = false;
```

This property enable or disable the index feature which list files of a static 
content directory when no file has been selected and `DefaultDocument` does not exists.
