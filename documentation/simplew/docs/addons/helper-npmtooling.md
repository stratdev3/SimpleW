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
| `SourcePath` | `../client` | Directory containing package.json, relative to `AppContext.BaseDirectory` or absolute. |
| `ProductionPath` | `wwwroot` | Compiled files, relative to the application directory. |
| `ApiUrl` | `/api` | API path relative to the active SimpleW origin, or a complete HTTP(S) URL used unchanged. Its path determines CORS scope and SPA fallback exclusion. |

### Development

| Option | Default | Purpose |
| --- | --- | --- |
| `Script` | `dev` | npm script to launch. |
| `ScriptArguments` | Empty list | Extra script arguments, passed after `--`. |
| `InstallDependencies` | `true` | Set to `false` to install dependencies yourself. |
| `EnableDevelopmentCors` | `true` | Allow frontend API calls; disable if you manage CORS yourself. |
| `OpenBrowser` | `true` | Open the browser when ready. |
| `StartupTimeout` | 60 seconds | How long to discover the URL and wait for the frontend to respond. |
| `InstallTimeout` | 10 minutes | Maximum dependency installation time. |
| `NodePath` | Auto-detected | Explicit Node executable path. |
| `NpmCliPath` | Auto-detected | Explicit path to `npm-cli.js`. |

### Logging

| Option | Default logger | Purpose |
| --- | --- | --- |
| `ServerLogger` | `SimpleW.Helper.NpmTooling.Server` | Startup, shutdown and supervision errors. |
| `ClientLogger` | `SimpleW.Helper.NpmTooling.Client` | npm output. |


## Minimal Development Example

### C# server

In `Program.cs`, register the module before starting the server:

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.NpmTooling;

var server = new SimpleWServer(IPAddress.Loopback, 8080);

server.MapGet("/api/hello", () => new { message = "Hello!" });

server.UseNpmToolingModule(options => {
    options.Environment = "Development";
    options.SourcePath = @"C:\projects\my-app\client";
});

await server.RunAsync();
```

Set `options.SourcePath` to the full (absolute) path of the folder containing your frontend's `package.json`, not to the file itself.

`options.Environment = "Development"` enables development mode: the module starts your frontend with `npm run dev` when SimpleW starts. This example sets the mode directly in C#, so no IDE configuration is needed.

### JavaScript client

For a Vite frontend, ensure `package.json` contains a `dev` script: `"dev": "vite"`.

Keep the host and port in your frontend configuration. NpmTooling reads the actual local HTTP(S) address announced by the script on stdout or stderr, including a different port selected by the frontend when one is occupied. You do not configure this address in C#.

The detector recognizes `Local: ...` (including Vite's default output), `Loopback: ...` and announcements such as `Listening on http://localhost:3000/`. It handles terminal colors, IPv4/IPv6 and application base paths. Only loopback URLs are used; wildcard bind addresses are converted to loopback. Unrelated log links and network URLs are ignored. Keep startup address logging enabled: a silent server or an unrecognized custom format will time out with a diagnostic instead of guessing a port. HTTPS requires a certificate trusted by the backend machine.

The module opens the browser itself. If your frontend also opens it, disable that feature there (for example `server.open: false` with Vite).

The module supplies the same API address through two environment variables: `SIMPLEW_API_URL` for any frontend tool and `VITE_SIMPLEW_API_URL` for Vite. It also enables development CORS. With Vite, use `VITE_SIMPLEW_API_URL` to call your API from the client:

```js
const response = await fetch(`${import.meta.env.VITE_SIMPLEW_API_URL}/hello`);
const data = await response.json();
```

In the example above, this calls `http://127.0.0.1:8080/api/hello`.

Other frontend tools can read `process.env.SIMPLEW_API_URL` in their Node.js configuration and expose it to browser code using their own mechanism. `SIMPLEW_API_URL` is not automatically available in browser code.

Use `ApiUrl` to change the API address:

```csharp
options.ApiUrl = "/services"; // Resolves to the active SimpleW origin + /services.
options.ApiUrl = "https://backend.example/services"; // Used unchanged, with no additional /api.
```

The default is `/api`. A path uses the actual SimpleW listener address and port, with wildcard addresses mapped to loopback. A full HTTP(S) URL preserves the supplied path and trailing slash; it is also the way to supply an HTTP endpoint when SimpleW uses a Unix socket. Credentials, whitespace, queries and fragments are not accepted.

The path in `ApiUrl` defines the CORS scope on SimpleW and the API namespace excluded from SPA fallback. A root address (`/` or a URL without a path) does not reserve the entire SPA namespace; explicit backend routes retain priority. The option does not create or rewrite routes. An API hosted on another server needs its own CORS configuration there.

### Start the application

Start the C# application from your IDE with the development profile. You do not need to run npm separately. The module:

1. Installs missing dependencies with `npm ci --include=dev` if a lockfile exists, otherwise `npm install --include=dev`.
2. Runs `npm run dev` and sends its output to SimpleW logs.
3. Detects the announced URL, waits for HTTP 2xx at that address, enables CORS for its exact origin and opens it in the browser.
4. Stops the frontend processes when SimpleW stops.

If the frontend fails, the backend stays available. The frontend is not restarted automatically.

In Development, NpmTooling always launches and owns the frontend. CORS allows no origin before readiness and clears the detected origin when the frontend exits or stops. A discovery/readiness timeout stops the owned processes and logs whether the address was missing or unreachable. See [Configuration options](#configuration-options) for other settings.

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
2. Runs `npm run build` with both `SIMPLEW_API_URL` and `VITE_SIMPLEW_API_URL` set to the value of `SimpleWNpmApiBaseUrl`.
3. Copies the contents of `dist` into `wwwroot` in the publish directory.

Node.js and npm must be available on the publishing machine. These steps do not run during a regular `dotnet build`.

The defaults expect `../client` as the frontend directory and `/api` as the API address. To change them, use these properties in the server's `.csproj`:

| Property | Default | Purpose |
| --- | --- | --- |
| `SimpleWNpmSourcePath` | `$(MSBuildProjectDirectory)/../client` | Frontend source directory. |
| `SimpleWNpmDistPath` | `$(SimpleWNpmSourcePath)/dist` | Compiled frontend directory. |
| `SimpleWNpmApiBaseUrl` | `/api` | API address passed to the frontend build. |
| `SimpleWNpmBuildClient` | `true` | Set to `false` to copy an already built frontend without running npm. |

Publish settings are separate from C# options: changing `SourcePath` does not change `SimpleWNpmSourcePath`. Keep `ApiUrl` and `SimpleWNpmApiBaseUrl` consistent for the target environment; both accept a path or a complete HTTP(S) URL.

### Run

Deploy the publish directory, including `wwwroot`, and start the application.

SimpleW serves the frontend from `wwwroot` beside the application. It requires an `index.html` there. For browser navigation such as `/dashboard`, it returns `index.html` so the frontend can handle the route. Unknown API routes and missing assets still return 404.
