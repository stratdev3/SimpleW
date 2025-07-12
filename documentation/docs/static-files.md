# Static Files

## Basic example

To serve statics files with very few lines of code :

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve static content located in your folder "C:\www\spa\" to "/" endpoint
            server.AddStaticContent(@"C:\www\spa\", "/");

            // enable autoindex if no index.html exists in the directory
            server.AutoIndex = true;

            // start non blocking background server
            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}
```

Then just point your browser to http://localhost:2015/.

Note : if `AutoIndex` is false and the directory does not contain a default document `index.html`, an http 404 error will return.

Note : on Windows, the Firewall can block this simple console app even if exposed on localhost and port > 1024. You need to allow access otherwise you will not reach the server.

## Multiples Directories

SimpleW can handle multiple directories as soon as they are declared under different endpoints.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directories/endpoints
            server.AddStaticContent(@"C:\www\frontend", "/");
            server.AddStaticContent(@"C:\www\public", "/public/");

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}
```


## Options

You can change some settings before server start.

To change the default document
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
