# Serve Static Files

Not only can you serve all your static files, but SimpleW is also great at serving your JavaScript applications — whether they're built with Vue, React, or anything else.

That's the goal of the [`StaticFilesModule`](../reference/staticfilesmodule.md).


## Basic

To serve statics files with very few lines of code :

```csharp{4,15-20}
using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // use the StaticFilesModule
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\spa\";                  // serve your files located here
                options.Prefix = "/";                           // to "/" endpoint
                options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
                options.AutoIndex = true;                       // enable autoindex if no index.html exists in the directory
            });

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }
}
```

Then just point your browser to http://localhost:2015/.

Note : if `AutoIndex` is false and the directory does not contain a default document `index.html`, an http 404 error will return.

Note : on Windows, the Firewall can block this simple console app even if exposed on localhost and port > 1024. You need to allow access otherwise you will not reach the server.


## Multiple Directories

SimpleW can handle multiple directories as soon as they are declared under different endpoints.

```csharp{4,15-24}
using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directories/endpoints
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\frontend\";
                options.Prefix = "/";
                options.CacheTimeout = TimeSpan.FromDays(1);
            });
            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\public\";
                options.Prefix = "/public/";
                options.CacheTimeout = TimeSpan.FromDays(1);
            });

            server.OnStarted(s => {
                Console.WriteLine("server started at http://localhost:{server.Port}/");
            });

            await server.RunAsync();
        }
    }
}
```


## Options

You can change some settings before server start.

To change the default document `index.html` by your own page
```csharp
option.DefaultDocument = "maintenance.html";
```


## Cache

By default, the `StaticFilesModule()` serves directories/files from disk to each request.
To enable cache, set the `CacheTimeout` property to anything but null.<br />

The following example enable cache for 1 day :

```csharp
// serve statics files
server.UseStaticFilesModule(options => {
    options.Path = @"C:\www\";                      // serve your files located here
    options.Prefix = "/";                           // to "/" endpoint
    options.CacheFilter = "*.csv";                  // cache only csv files
    options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
});
```

::: tip NOTE
When cache is enabled, an internal filesystem watcher is keeping this cache up-to-date.
It supports realtime file editing even when specific lock/write occurs.
:::


## How to serve a Vue app

This section shows how to server a vue.js application.

First, create your vue/vite application :

```bash
$ npm create vite@latest my-vue-app

> npx
> create-vite my-vue-app
│
◇  Select a framework:
│  Vue
│
◇  Select a variant:
│  JavaScript
│
◇  Use rolldown-vite (Experimental)?:
│  No
│
◇  Install with npm and start now?
│  Yes
│
◇  Scaffolding project in C:\www\toto\myapp\my-vue-app...
│
◇  Installing dependencies with npm...

added 35 packages, and audited 36 packages in 5s

6 packages are looking for funding
  run `npm fund` for details

found 0 vulnerabilities
│
◇  Starting dev server...

> my-vue-app@0.0.0 dev
> vite

  VITE v7.1.12  ready in 397 ms

  ➜  Local:   http://localhost:5173/
  ➜  Network: use --host to expose
  ➜  press h + enter to show help
```

Node will create all files and start serving the application using its own built-in web server.
But we want SimpleW to server the application in a production mode.

So, build the application release :

```bash
$ cd my-vue-app
$ npm run build

> my-vue-app@0.0.0 build
> vite build

vite v7.1.12 building for production...
✓ 16 modules transformed.
dist/index.html                  0.46 kB │ gzip:  0.29 kB
dist/assets/index-CIkLTZfM.css   1.26 kB │ gzip:  0.64 kB
dist/assets/index-Cc-rvdMb.js   61.31 kB │ gzip: 24.69 kB
✓ built in 424ms
```

To get the location of dist files just

```bash
$ pwd
> C:\www\my-vue-app\dist
```

Now, we will server this directory using the `StaticFilesModule` module of SimpleW :

```csharp{15}
using System;
using System.Net;
using SimpleW;
using SimpleW.Modules;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            server.UseStaticFilesModule(options => {
                options.Path = @"C:\www\my-vue-app\dist\";      // serve your files located here
                options.Prefix = "/";                           // to "/" endpoint
                options.CacheTimeout = TimeSpan.FromDays(1);    // cached for 24h
                options.AutoIndex = true;                       // enable autoindex if no index.html exists in the directory
            });

            server.OnStarted(s => {
                Console.WriteLine("server started at http://localhost:{server.Port}/");
            });

            // start a blocking background server
            await server.RunAsync();
        }
    }
}
```

Open your browser to http://localhost:2015/ and you will see your vue.js app.

::: tip NOTE
You can update any files in this directory without having to reload SimpleW.
:::

