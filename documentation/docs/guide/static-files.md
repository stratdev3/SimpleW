# Serve Static Files

## Basic

To serve statics files with very few lines of code :

::: code-group

<<< @/snippets/static-files.cs#basic{csharp:line-numbers} [program.cs]

:::

Then just point your browser to http://localhost:2015/.

Note : if `AutoIndex` is false and the directory does not contain a default document `index.html`, an http 404 error will return.

Note : on Windows, the Firewall can block this simple console app even if exposed on localhost and port > 1024. You need to allow access otherwise you will not reach the server.


## Multiple Directories

SimpleW can handle multiple directories as soon as they are declared under different endpoints.

::: code-group

<<< @/snippets/static-files-multiple-directories.cs#basic{csharp:line-numbers} [program.cs]

:::


## Options

You can change some settings before server start.

To change the default document `index.html` by your own page
```csharp:line-numbers
server.DefaultDocument = "maintenance.html";
```

To add custom mime types

```csharp:line-numbers
server.AddMimeTypes(".vue", "text/html");
```


## Cache

The `AddStaticContent()` caches all directories/files in RAM (default: 1 hour) on server start.<br />
Also, an internal filesystem watcher is keeping this cache up-to-date.
It supports realtime file editing even when specific lock/write occurs.

To modify cache duration or to filter files

```csharp:line-numbers
// serve statics files
server.AddStaticContent(
    @"C:\www\",             // under C:\www or its subdirectories
    "/",                    // to / endpoint
    "*.csv",                // only CSV files
    TimeSpan.FromDays(1)    // set cache to 1 day
);
```
