# Serve Static Files

Not only can you serve all your static files, but SimpleW is also great at serving your JavaScript applications — whether they're built with Vue, React, or anything else.


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

By default, the `AddStaticContent()` serves directories/files from disk to each request.
To enable cache, set the `timeout` property to anything but null.<br />

The following example enable cache for 1 day :

```csharp:line-numbers
// serve statics files
server.AddStaticContent(
    @"C:\www\",             // under C:\www or its subdirectories
    "/",                    // to / endpoint
    "*.csv",                // only CSV files
    TimeSpan.FromDays(1)    // set cache to 1 day
);
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

Now, we will server this directory using the `AddStaticFiles` module of SimpleW :

::: code-group

<<< @/snippets/static-files-vuejs.cs#basic{csharp:line-numbers} [program.cs]

:::

Open your browser to http://localhost:2015/ and you will see your vue.js app.

::: tip NOTE
You can update any files in this directory without having to reload SimpleW.
:::

