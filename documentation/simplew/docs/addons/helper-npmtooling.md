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

NpmTooling supports local development and deployment:

- **Development: runs your frontend while you develop locally.**
  - Starts the frontend through npm and stops it with SimpleW.
  - Captures npm logs and opens the browser.
- **Build and Deploy: builds your application and serves it after deployment.**
  - Builds and copies frontend files during publication.
  - Serves the compiled frontend and its navigation routes when the deployed application runs.

## Requirements

| Usage | Requirements |
| --- | --- |
| SimpleW application | .NET 8, 9 or 10 and SimpleW 26.1.x. |
| Local development | Node.js 22.12 or later, npm, and an existing frontend with a `dev` script. |
| Publication (build for deployment) | Node.js and npm on the publishing machine's `PATH`, and a frontend `build` script. |
| Running the deployed application | Published static files and the module's `Production` mode. Node.js and npm are not needed on the deployment machine. |

## Installation

```sh
dotnet add package SimpleW.Helper.NpmTooling
```

See the [changelog](./helper-npmtooling-changelog.md).


## Configuration options

Set these options in the `UseNpmToolingModule(options => { ... })` callback before starting SimpleW. Publication properties are listed under [Build and Deploy](#build-and-deploy).

### Environment and paths

| Option | Default | Purpose |
| --- | --- | --- |
| `Environment` | `NpmEnvironment.AutoDetect` | Automatic: `Production` for an application marked by `dotnet publish`, otherwise `Development`. Use `NpmEnvironment.Development` or `NpmEnvironment.Production` to force a mode. |
| `SourcePath` | `../client` | Path to the directory containing `package.json`, either absolute or relative to `AppContext.BaseDirectory`. |
| `ProductionPath` | `wwwroot` | Directory containing compiled files, relative to the application directory. |
| `ApiUrl` | `/api` | API path relative to the active SimpleW origin, or a complete HTTP(S) URL used unchanged. Its path determines CORS scope and SPA fallback exclusion. |

### Development options

| Option | Default | Purpose |
| --- | --- | --- |
| `Script` | `dev` | npm script to launch. |
| `ScriptArguments` | Empty list | Extra script arguments, passed after `--`. |
| `InstallDependencies` | `true` | Set to `false` to install dependencies yourself. |
| `EnableDevelopmentCors` | `true` | Allow frontend API calls; disable if you manage CORS yourself. |
| `OpenBrowser` | `true` | Open the browser when ready. |
| `StartupTimeout` | 60 seconds | Maximum time allowed to discover the URL and wait for the frontend to respond. |
| `InstallTimeout` | 10 minutes | Maximum dependency installation time. |
| `NodePath` | Auto-detected | Explicit Node executable path. |
| `NpmCliPath` | Auto-detected | Explicit path to `npm-cli.js`. |

### Logging

| Option | Default logger | Purpose |
| --- | --- | --- |
| `ServerLogger` | `SimpleW.Helper.NpmTooling.Server` | Startup, shutdown and supervision errors. |
| `ClientLogger` | `SimpleW.Helper.NpmTooling.Client` | npm output. |


## Development

In development mode, NpmTooling manages npm and the frontend development server so you can focus on writing code in your IDE.

### C# server

In `Program.cs`, register the module before starting the server. Use this same configuration for local development and deployment:

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.NpmTooling;

var server = new SimpleWServer(IPAddress.Loopback, 8080);

server.MapGet("/api/hello", () => new { message = "Hello!" });

server.UseNpmToolingModule(options => {
    // Folder containing your frontend's package.json.
    options.SourcePath = @"C:\projects\my-app\client";
    // If your frontend calls the backend API, provide its relative or absolute URL.
    options.ApiUrl = "/api";
});

await server.RunAsync();
```

### JavaScript client

NpmTooling manages your frontend; no changes are needed to run it through the module.

If your frontend calls an API provided by the C# server, it needs the API's base URL.
To avoid hardcoding that URL, NpmTooling provides an environment variable: `VITE_SIMPLEW_API_URL`.

Example:

```js
const response = await fetch(`${import.meta.env.VITE_SIMPLEW_API_URL}/hello`);
const data = await response.json();
```

In the example above, this calls `http://127.0.0.1:8080/api/hello`.

If you change `ApiUrl` on the server side, your frontend receives the updated address the next time it is started by NpmTooling.

### Run locally

Build and start the C# application from your IDE in either Debug or Release. No environment variable or special launch profile is required.

<ZoomableImage
  src="https://raw.githubusercontent.com/stratdev3/storage/refs/heads/master/simplew/modules/SimpleW.Helper.NpmTooling-Development-Debug.gif"
  alt="Debugging a frontend with SimpleW NpmTooling in development mode"
/>


## Build and Deploy

`dotnet publish` builds the frontend and copies it into the published application. NpmTooling then serves those files automatically. It uses the [`staticFileModule`](../guide/staticfiles.md) behind the hood.

### C# server

In `Program.cs`:

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.NpmTooling;

var server = new SimpleWServer(IPAddress.Loopback, 8080);

server.MapGet("/api/hello", () => new { message = "Hello!" });

server.UseNpmToolingModule(options => {
    options.ProductionPath = "wwwroot";
    options.ApiUrl = "/api";
});

await server.RunAsync();
```

`ProductionPath` points to the compiled frontend beside the executable. No additional `StaticFilesModule` registration is needed.

In the server's `.csproj`, add this inside `<Project>`:

```xml
<PropertyGroup>
  <SimpleWNpmSourcePath>C:\projects\my-app\client</SimpleWNpmSourcePath>
  <SimpleWNpmDistPath>$(SimpleWNpmSourcePath)/dist</SimpleWNpmDistPath>
  <SimpleWNpmApiBaseUrl>/api</SimpleWNpmApiBaseUrl>
  <SimpleWNpmBuildClient>true</SimpleWNpmBuildClient>
</PropertyGroup>
```

`SimpleWNpmSourcePath` is the full path to the folder containing `package.json`. `SimpleWNpmDistPath` is the frontend build's output folder. The NuGet package imports the targets automatically.

### JavaScript client

In your client code:

```js
const response = await fetch(`${import.meta.env.VITE_SIMPLEW_API_URL}/hello`);
const data = await response.json();
```

`SimpleWNpmApiBaseUrl` supplies the API address during the build through `VITE_SIMPLEW_API_URL`. Vite embeds this value in the JavaScript: `/api` makes this request call `/api/hello` on the same domain. To change it, update the property and publish again.

### Publish the application

From the server project:

```sh
dotnet publish -c Release -o ./publish
```

NpmTooling installs frontend dependencies, runs `npm run build`, and copies the generated files into `publish/wwwroot`. Node.js and npm are required on the publishing machine.

`-o` selects the publish directory; the frontend destination is its `wwwroot` subfolder. Set `SimpleWNpmBuildClient=false` to copy an already built frontend without running npm.

### Run the deployed application

Deploy the entire `publish` directory and start the executable.

```sh
dotnet MyWebApp.dll
```

With this example, open `http://localhost:8080/` on the server machine. Adjust the listening address if clients connect from other machines.

NpmTooling serves `wwwroot` and handles SPA navigation. Node.js and npm are not needed on the deployment machine.

## One Configuration for Both

Use a single setup for both local development and deployment.

### C# server

In `Program.cs`:

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.NpmTooling;

var server = new SimpleWServer(IPAddress.Loopback, 8080);

server.MapGet("/api/hello", () => new { message = "Hello!" });

server.UseNpmToolingModule(options => {
    options.SourcePath = @"C:\projects\my-app\client";
    options.ProductionPath = "wwwroot";
    options.ApiUrl = "/api";
});

await server.RunAsync();
```

In the server's `.csproj`, inside `<Project>`:

```xml
<PropertyGroup>
  <SimpleWNpmSourcePath>C:\projects\my-app\client</SimpleWNpmSourcePath>
  <SimpleWNpmDistPath>$(SimpleWNpmSourcePath)/dist</SimpleWNpmDistPath>
  <SimpleWNpmApiBaseUrl>/api</SimpleWNpmApiBaseUrl>
  <SimpleWNpmBuildClient>true</SimpleWNpmBuildClient>
</PropertyGroup>
```

Use the full path to the folder containing `package.json` in both source paths. `ApiUrl` supplies the API address during development; `SimpleWNpmApiBaseUrl` supplies it during the frontend build.

### JavaScript client

Use the same API call in both modes:

```js
const response = await fetch(`${import.meta.env.VITE_SIMPLEW_API_URL}/hello`);
const data = await response.json();
```
