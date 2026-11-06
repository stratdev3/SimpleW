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

Configure paths and the API URL in the server's `.csproj`. The NuGet package embeds them in the application at each build; rebuild after changing them. See [Build and Deploy](#build-and-deploy) for publication.

### Project properties

| Property | Default | Purpose |
| --- | --- | --- |
| `SimpleWNpmSourcePath` | `../client` | Folder containing `package.json`, absolute or relative to the `.csproj`. |
| `SimpleWNpmDistPath` | `$(SimpleWNpmSourcePath)/dist` | Frontend build output, absolute or relative to the `.csproj`. Used during publication only. |
| `SimpleWNpmPublishPath` | `wwwroot` | Destination subfolder inside the published application, also used when serving files. Absolute paths and paths outside that directory are rejected. |
| `SimpleWNpmApiBaseUrl` | `/api` | API path or complete HTTP(S) URL, shared by development and the frontend build. Its path determines CORS scope and SPA fallback exclusion. |
| `SimpleWNpmBuildClient` | `true` | Build the frontend during publication; `false` copies an already built frontend. |

### C# options

The remaining options use the optional `UseNpmToolingModule(options => { ... })` callback.

| Option | Default | Purpose |
| --- | --- | --- |
| `Environment` | `NpmEnvironment.AutoDetect` | Automatic: `Production` for an application marked by `dotnet publish`, otherwise `Development`. Use `NpmEnvironment.Development` or `NpmEnvironment.Production` to force a mode. |
| `Script` | `dev` | npm script to launch. |
| `ScriptArguments` | Empty list | Extra script arguments, passed after `--`. |
| `InstallDependencies` | `true` | Set to `false` to install dependencies yourself. |
| `EnableDevelopmentCors` | `true` | Allow frontend API calls; disable if you manage CORS yourself. |
| `OpenBrowser` | `true` | Open the browser when ready. |
| `StartupTimeout` | 60 seconds | Maximum time allowed to discover the URL and wait for the frontend to respond. |
| `InstallTimeout` | 10 minutes | Maximum dependency installation time. |
| `NodePath` | Auto-detected | Explicit Node executable path. |
| `NpmCliPath` | Auto-detected | Explicit path to `npm-cli.js`. |
| `ServerLogger` | `SimpleW.Helper.NpmTooling.Server` | Startup, shutdown and supervision errors. |
| `ClientLogger` | `SimpleW.Helper.NpmTooling.Client` | npm output. |

Mode selection is independent of Debug/Release. The publication marker stays with the binary, including when copied or started from `bin`; a regular build resets it. Automatic publication detection supports the `dotnet publish` CLI; IDE publishing has not been verified.


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

// just load the module
server.UseNpmToolingModule();

await server.RunAsync();
```

In the server's `.csproj`, inside `<Project>`:

```xml
<PropertyGroup>
  <!-- full path of the folder containing package.json -->
  <SimpleWNpmSourcePath>C:\projects\my-app\client</SimpleWNpmSourcePath>
  <!-- if your client consume the server's API, set the relative or absolute URL.
       This URL will be passed as VITE_SIMPLEW_API_URL environment var. -->
  <SimpleWNpmApiBaseUrl>/api</SimpleWNpmApiBaseUrl>
</PropertyGroup>
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

If you change `SimpleWNpmApiBaseUrl`, rebuild and restart the C# application so the frontend receives the updated address.

### Run locally

Build and start the C# application from your IDE in either Debug or Release. No environment variable or special launch profile is required.

<ZoomableImage
  src="https://raw.githubusercontent.com/stratdev3/storage/refs/heads/master/simplew/modules/SimpleW.Helper.NpmTooling-Development-Debug.gif"
  alt="Debugging a frontend with SimpleW NpmTooling in development mode"
/>


## Build and Deploy

`dotnet publish` prepares the application and frontend files for deployment. When the published application runs, NpmTooling serves those files through the [`StaticFilesModule`](../guide/staticfiles.md). This is `Production` mode, including on staging servers.

### C# server

In `Program.cs`:

```csharp
using System.Net;
using SimpleW;
using SimpleW.Helper.NpmTooling;

var server = new SimpleWServer(IPAddress.Loopback, 8080);

server.MapGet("/api/hello", () => new { message = "Hello!" });

server.UseNpmToolingModule();

await server.RunAsync();
```

No additional `StaticFilesModule` registration is needed.

In the server's `.csproj`, add this inside `<Project>`:

```xml
<PropertyGroup>
  <SimpleWNpmSourcePath>C:\projects\my-app\client</SimpleWNpmSourcePath>
  <SimpleWNpmDistPath>$(SimpleWNpmSourcePath)/dist</SimpleWNpmDistPath>
  <SimpleWNpmPublishPath>wwwroot</SimpleWNpmPublishPath>
  <SimpleWNpmApiBaseUrl>/api</SimpleWNpmApiBaseUrl>
  <SimpleWNpmBuildClient>true</SimpleWNpmBuildClient>
</PropertyGroup>
```


### JavaScript client

In your client code:

```js
const response = await fetch(`${import.meta.env.VITE_SIMPLEW_API_URL}/hello`);
const data = await response.json();
```

### Publish the application

<ZoomableImage
  src="https://raw.githubusercontent.com/stratdev3/storage/refs/heads/master/simplew/modules/SimpleW.Helper.NpmTooling-Publish-Release.gif"
  alt="Publishing a frontend with SimpleW NpmTooling in release mode"
/>

From the server project:

```sh
dotnet publish -c Release -o ./publish
```

NpmTooling installs frontend dependencies, runs `npm run build`, and copies the generated files into `publish/wwwroot`. Node.js and npm are required on the publishing machine.

`-o` selects the publish directory; `SimpleWNpmPublishPath` selects its frontend subfolder. Set `SimpleWNpmBuildClient=false` to copy an already built frontend without running npm.

`--no-build` requires a previously published binary with matching source path, publish path and API URL. Otherwise, publication stops before npm; run `dotnet publish` without `--no-build`.

### Run the deployed application

Deploy the entire `publish` directory and start the executable.

```sh
dotnet MyWebApp.dll
```

With this example, open `http://localhost:8080/` on the server machine. Adjust the listening address if clients connect from other machines.

NpmTooling serves `wwwroot` in this example and handles SPA navigation. The folder is resolved beside the executable, so you can move the entire deployment. Node.js and npm are not needed on the deployment machine.

:::tip INFO
Use this configuration when you need both local development and deployment. With the default `AutoDetect` mode, running a regular build starts the frontend through npm; running the published application serves the compiled files. No configuration changes are needed between the two.
:::