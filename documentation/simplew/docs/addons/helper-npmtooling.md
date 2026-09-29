# NpmTooling

<AddonMeta
    package="SimpleW.Helper.NpmTooling"
    status="official"
    license="MIT"
    license-url="https://github.com/Stratdev3/SimpleW/blob/master/licence"
    experimental
/>

**SimpleW.Helper.NpmTooling** manages a JavaScript frontend alongside your SimpleW application.

## Features

Divided in two modes:

- **Development: helps you developping your full stack web app.**
  - Starts the frontend through npm and stops it with SimpleW.
  - Capture npm logs and open the browser.
- **Production: helps you packaging the whole web app.**
  - Build and copy frontend files during publication.
  - Serve the published frontend and its navigation routes.

## Requirements

| Usage | Requirements |
| --- | --- |
| SimpleW application | .NET 8, 9 or 10 and SimpleW 26.1.x. |
| Development | Node.js 22.12 or later, npm, and an existing frontend with a `dev` script. |
| Publication | Node.js and npm on `PATH`, and a frontend `build` script. |
| Production | Published static files. Node.js and npm are not needed. |

## Installation

```sh
dotnet add package SimpleW.Helper.NpmTooling
```

See the [changelog](./helper-npmtooling-changelog.md).


## Configuration options

Set these in the `UseNpmToolingModule(options => { ... })` callback, before starting SimpleW. Publication properties are listed under [Minimal Production Example](#minimal-production-example).

### Environment and paths

| Option | Default | Purpose |
| --- | --- | --- |
| `Environment` | `null` | Uses `DOTNET_ENVIRONMENT`, then `Production`. |
| `ContentRootPath` | Current working directory | Base directory for `Spa.SourcePath`. |
| `Spa.SourcePath` | `../client` | Frontend source directory. |
| `Spa.ProductionPath` | `wwwroot` | Compiled files, relative to the application directory. |
| `Spa.ApiPrefix` | `/api` | API prefix, excluded from frontend navigation fallback. |

### Development

| Option | Default | Purpose |
| --- | --- | --- |
| `Spa.DevelopmentUrl` | `http://localhost:5173` | Local frontend address to wait for and open. |
| `Spa.BackendUrl` | `null` | Override the API origin; otherwise use the SimpleW listener. |
| `Spa.Script` | `dev` | npm script to launch. |
| `Spa.ScriptArguments` | Empty list | Extra script arguments, passed after `--`. |
| `Spa.LaunchDevelopmentServer` | `true` | Set to `false` if you start the frontend yourself. |
| `Spa.InstallDependencies` | `true` | Set to `false` to install dependencies yourself. |
| `Spa.EnableDevelopmentCors` | `true` | Allow frontend API calls; disable if you manage CORS yourself. |
| `Spa.OpenBrowser` | `true` | Open the browser when ready. |
| `Spa.StartupTimeout` | 60 seconds | How long to wait for the frontend. |
| `Spa.InstallTimeout` | 10 minutes | Maximum dependency installation time. |
| `Spa.NodePath` | Auto-detected | Explicit Node executable path. |
| `Spa.NpmCliPath` | Auto-detected | Explicit path to `npm-cli.js`. |

### Logging

| Option | Default logger | Purpose |
| --- | --- | --- |
| `ServerLogger` | `SimpleW.Helper.NpmTooling.Server` | Startup, shutdown and supervision errors. |
| `ClientLogger` | `SimpleW.Helper.NpmTooling.Client` | npm output. |


## Minimal Development Example

### C# server

In `Program.cs`, register the module before starting the server. This example assumes a `client` folder next to your server project:

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.NpmTooling;

var server = new SimpleWServer(IPAddress.Loopback, 8080);

server.MapGet("/api/hello", () => new { message = "Hello!" });

server.UseNpmToolingModule(options => {
    options.Spa.SourcePath = "../client";
    options.Spa.DevelopmentUrl = "http://localhost:5173";
});

await server.RunAsync();
```

`SourcePath` is relative to the working directory. Start from the server project directory, or provide an absolute path.

Set `DOTNET_ENVIRONMENT` to `Development` in your IDE's launch profile. The module uses this variable unless you set `options.Environment` explicitly. Any other value, or no value, selects production mode.

### JavaScript client

For a Vite frontend, ensure `package.json` contains a `dev` script: `"dev": "vite"`.

Your frontend must listen at `DevelopmentUrl`. With Vite, add these settings to your existing `vite.config.js` configuration:

```js
server: {
    host: 'localhost',
    port: 5173,
    strictPort: true,
    open: false
}
```

`strictPort` prevents Vite from silently choosing another port. The module opens the browser itself.

Use `VITE_SIMPLEW_API_URL` to call your API from the client. The module supplies this address and enables development CORS:

```js
const response = await fetch(`${import.meta.env.VITE_SIMPLEW_API_URL}/hello`);
const data = await response.json();
```

In the example above, this calls `http://127.0.0.1:8080/api/hello`.

### Start the application

Start the C# application from your IDE with the development profile. You do not need to run npm separately. The module:

1. Installs missing dependencies with `npm ci --include=dev` if a lockfile exists, otherwise `npm install --include=dev`.
2. Runs `npm run dev` and sends its output to SimpleW logs.
3. Waits for the frontend to respond, then opens `http://localhost:5173` in the browser.
4. Stops the frontend processes when SimpleW stops.

If the frontend fails, the backend stays available. The frontend is not restarted automatically.

To start the frontend yourself, set `Spa.LaunchDevelopmentServer = false` and supply `VITE_SIMPLEW_API_URL` in its environment. The module waits for it but does not stop it. See [Configuration options](#configuration-options) for other settings.

## Minimal Production Example

Keep the same module registration. Production uses compiled HTML, CSS and JavaScript files; it does not run a Node server.

### Publish

Your frontend needs a `build` script that generates `dist/index.html` and its assets. For Vite, use `"build": "vite build"` in `package.json`.

From the server project, run:

```sh
dotnet publish -c Release
```

With the NuGet package installed, publication automatically:

1. Installs dependencies with `npm ci --include=dev` or `npm install --include=dev`.
2. Runs `npm run build`.
3. Copies the contents of `dist` into `wwwroot` in the publish directory.

Node.js and npm must be available on the publishing machine. These steps do not run during a regular `dotnet build`.

The defaults expect `../client` as the frontend directory and `/api` as the API address. To change them, use these properties in the server's `.csproj`:

| Property | Default | Purpose |
| --- | --- | --- |
| `SimpleWNpmSourcePath` | `$(MSBuildProjectDirectory)/../client` | Frontend source directory. |
| `SimpleWNpmDistPath` | `$(SimpleWNpmSourcePath)/dist` | Compiled frontend directory. |
| `SimpleWNpmApiBaseUrl` | `/api` | API address passed to the frontend build. |
| `SimpleWNpmBuildClient` | `true` | Set to `false` to copy an already built frontend without running npm. |

Publish settings are separate from C# options: changing `Spa.SourcePath` does not change `SimpleWNpmSourcePath`. For a custom API prefix, set both `Spa.ApiPrefix` and `SimpleWNpmApiBaseUrl`.

### Run

Deploy the publish directory, including `wwwroot`, and start the application with `DOTNET_ENVIRONMENT=Production` or with the variable unset. Node.js and npm are no longer needed.

SimpleW serves the frontend from `wwwroot` beside the application. It requires an `index.html` there. For browser navigation such as `/dashboard`, it returns `index.html` so the frontend can handle the route. Unknown API routes and missing assets still return 404.
