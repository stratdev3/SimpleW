# SimpleW.Helper.NpmTooling

[![website](https://raw.githubusercontent.com/stratdev3/SimpleW/refs/heads/master/documentation/simplew/docs/public/simplew-og.png)](https://simplew.net)

[![License](https://img.shields.io/badge/license-MIT-7737d1.svg)](https://github.com/Stratdev3/SimpleW/blob/master/licence)
[![NuGet](https://img.shields.io/nuget/v/SimpleW.Helper.NpmTooling?color=7737d1&label=version&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Helper.NpmTooling)
[![Downloads](https://img.shields.io/nuget/dt/SimpleW.Helper.NpmTooling?color=7737d1&label=downloads&logo=NuGet)](https://www.nuget.org/packages/SimpleW.Helper.NpmTooling)

<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

A SimpleW add-on for managing an npm frontend during development and serving its compiled files in production. The browser calls the SimpleW API directly. No API proxy or JavaScript integration plugin is required.

## Standalone usage

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.NpmTooling;

var server = new SimpleWServer(IPAddress.Loopback, 8080);
server.MapGet("/api/hello", () => new { message = "Hello" });
server.UseNpmToolingModule(options => {
    options.Environment = "Development";
    options.SourcePath = "../client"; // Relative to AppContext.BaseDirectory, or use an absolute path.
});
await server.RunAsync();
```

If no environment is specified explicitly, the module reads `DOTNET_ENVIRONMENT`, falling back to `Production`. Only `Development`, matched case-insensitively, enables npm. Call `UseNpmToolingModule` once, before starting the server. The IDE still launches the C# server, so backend breakpoints remain available.

Development prerequisites: Node >= 22.12 and npm, available on the IDE's PATH. The module targets .NET 8, 9 and 10 and SimpleW 26.1.x. It supports frontends that produce static files; it does not host a Node SSR server in production.

## API address

The module passes `VITE_SIMPLEW_API_URL` to the npm process, for example `http://127.0.0.1:8080/api`. The origin comes from the active SimpleW listener, or from `BackendUrl` when specified. `ApiPrefix` is appended to that origin. An address bound to all interfaces is replaced with a loopback address for local development.

In a SPA using Vite:

```js
const apiBaseUrl = import.meta.env.VITE_SIMPLEW_API_URL;
if (!apiBaseUrl) throw new Error('VITE_SIMPLEW_API_URL is missing.');
const response = await fetch(`${apiBaseUrl}/hello`);
```

Vite exposes this variable to client code natively. It is passed only to the child process, without changing the system environment or writing a configuration file. Other frontend tools can read this variable through their own configuration; the module does not require a plugin.

The frontend chooses its own host and port. The module discovers the actual local HTTP(S) URL from the development script's stdout or stderr, verifies that it responds, then opens that URL and enables CORS for its origin. No frontend URL is configured in C# and no frontend configuration file is rewritten. A different port chosen by the frontend is detected automatically.

Startup announcements such as `Local: http://localhost:5173/` (Vite's default output), `Loopback: ...`, or `Listening on http://127.0.0.1:3000/` are recognized, including terminal colors, IPv6 and application base paths. Only loopback URLs are accepted; wildcard bind addresses `0.0.0.0` and `[::]` are converted to their loopback counterparts. Arbitrary URLs in npm command echoes, documentation links and network announcements are ignored. The development script must keep its address announcement enabled; custom/silent log formats are not universally discoverable. HTTPS certificates must be trusted by the machine running the backend.

Keep your existing frontend configuration and plugins. If the frontend also opens a browser, disable that feature there (for example `server.open: false` in Vite) to let NpmTooling open it once. Do not add an `/api` proxy for this integration.

## Options

| Option | Default | Purpose |
| --- | --- | --- |
| `Environment` | DOTNET_ENVIRONMENT, otherwise Production | Select development or production |
| `SourcePath` | `../client` | Directory containing package.json, relative to AppContext.BaseDirectory or absolute |
| `ProductionPath` | `wwwroot` | Absolute path or path relative to the executable directory |
| `BackendUrl` | Listener origin | Explicit API origin override; required for a Unix socket |
| `ApiPrefix` | `/api` | REST prefix, CORS scope and exclusion from the SPA fallback |
| `Script` | `dev` | npm script to run |
| `ScriptArguments` | Empty list | Separate arguments after the npm `--` separator |
| `EnableDevelopmentCors` | `true` | Install development CORS rules |
| `InstallDependencies` | `true` | Install missing direct dependencies |
| `OpenBrowser` | `true` | Open the frontend once it is available over HTTP |
| `StartupTimeout` | 60 seconds | Maximum time to discover the URL and receive HTTP 2xx at its announced path |
| `InstallTimeout` | 10 minutes | Dependency installation time limit |
| `NodePath` / `NpmCliPath` | Automatic discovery | Explicit paths for custom installations |
| `ServerLogger` / `ClientLogger` | SimpleW.Helper.NpmTooling.Server and .Client loggers | Separate supervision messages and npm output |

Configure options before installing the module; do not change them while it is running. npm stderr output is logged as warnings; process failures are logged by the supervisor.

Relative source paths are resolved from `AppContext.BaseDirectory` (the executable directory), independently of the working directory. For example, `../client` points to a sibling of the executable directory, not the server project directory. In a typical development build, the executable is under `bin/Debug/netX.0/`; use an absolute source path or adjust the relative path accordingly. When migrating from `ContentRootPath`, replace the base and relative source path with a single absolute `SourcePath`.

### CORS

In development, the module allows only the discovered frontend origin on `ApiPrefix`, using SimpleW's existing CORS module. No origin is allowed until the frontend is ready; the origin is cleared on shutdown or process exit. Common REST requests and OPTIONS preflights are supported; allowed headers are `Content-Type, Authorization`. Credentials are disabled, and no wildcard origin is added.

If the application already configures CORS, set `EnableDevelopmentCors=false` and manage the rules in the application, particularly for cross-origin cookies. The module does not add CORS in production. It neither creates nor moves API routes: those remain defined by the application.

### Process supervision

If a direct dependency is missing from the client's `node_modules`, the module runs `npm ci --include=dev` when package-lock.json exists, or `npm install --include=dev` otherwise. Workspaces with packages hoisted to a parent directory can disable automatic installation. Changing a version in the manifest requires updating dependencies separately; detection checks their presence, not their versions.

The module invokes npm-cli.js through Node, without a shell, passing arguments separately. Its embedded supervisor monitors a pipe owned by the C# process. Windows also uses a Job Object; Unix uses a dedicated process group. Only processes created by the module are cleaned up, including when the debugger forcibly stops the backend. Processes are never terminated by program name or port number.

In Development, the module always launches and owns the frontend. The frontend handles occupied ports according to its own configuration: it can fail or select another port, whose announced URL will be discovered. NpmTooling does not scan ports or attach to a separately launched server. Startup announcements are trusted as coming from the configured script; HTTP availability alone does not prove process ownership.

If startup times out, the supervisor distinguishes between no recognized local URL and announced URLs that never became ready. It logs the failure and stops its owned processes. There is no fallback to a hard-coded port or wildcard CORS origin.

Installations and probes support cancellation. A failure leaves the backend available without automatically restarting the client. After correcting the problem, restart the server. Reloading a listener does not create a second frontend; changing the API origin requires a full restart to refresh the environment variable.

## Publishing

The package contains `buildTransitive` files that run only when publishing an executable, never during a regular build or IDE project load.

```xml
<PropertyGroup>
  <SimpleWNpmSourcePath>$(MSBuildProjectDirectory)/../client</SimpleWNpmSourcePath>
  <SimpleWNpmDistPath>$(SimpleWNpmSourcePath)/dist</SimpleWNpmDistPath>
  <SimpleWNpmBuildClient>true</SimpleWNpmBuildClient>
  <SimpleWNpmApiBaseUrl>/api</SimpleWNpmApiBaseUrl>
</PropertyGroup>
```

Publishing installs dependencies, then runs `npm run build` with `VITE_SIMPLEW_API_URL` set from `SimpleWNpmApiBaseUrl` and `NODE_ENV=production`. Vite embeds this value in the compiled JavaScript. `/api` uses the domain and port where SimpleW serves the application; an absolute URL can also be used for a separate deployment, with CORS configured in the application.

The dist files are added to the publish output under `wwwroot`, as external files even for a single-file publish. An npm failure or a missing dist/index.html fails publication. With `SimpleWNpmBuildClient=false`, npm is not called: a dist directory already built with the correct API address is required and copied unchanged.

MSBuild does not read C# runtime options. For a custom prefix, configure both `ApiPrefix` and `SimpleWNpmApiBaseUrl`. Likewise, keep `SourcePath` and `SimpleWNpmSourcePath` consistent. The legacy names `SimpleWStackSourcePath`, `SimpleWStackDistPath` and `SimpleWStackBuildClient` are accepted for migration; explicitly set new properties take precedence.

NuGet consumption imports the targets automatically. With ProjectReference, explicitly import `buildTransitive/SimpleW.Helper.NpmTooling.targets` into the executable project.

## Production

The module serves assets and reserves the index.html fallback for GET/HEAD navigation requests that accept HTML. API routes, missing assets and methods other than GET/HEAD do not become HTML pages. The public directory must contain index.html; links, junctions, path traversal and hidden paths are rejected. Keep assets immutable while serving them and publish to a clean output directory.

The module occupies the router's root fallback. Avoid another fallback or a global static route that would hide it. Vue routes containing a dot in their final segment do not receive the SPA fallback. Production requires neither Node, npm nor Vite.

## Validation to perform later

Automated discovery and process tests cover startup output, dynamic ports, base paths, CORS, timeouts, early exit, cancellation and cleanup. Before release, also validate F5, occupied-port fallback with your frontend, HTTPS, installation cancellation, forced debugger shutdown on Windows and Unix, publishing with the injected variable, publishing without npm and consuming the NuGet package in an independent application.
