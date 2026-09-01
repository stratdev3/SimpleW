---
title: Getting Started
description: Build and run your first SimpleW HTTP endpoint in a few minutes.
outline: deep
---

<script setup>
import GettingStartedTerminal from '../.vitepress/theme/components/GettingStartedTerminal.vue'
import GettingStartedNextSteps from '../.vitepress/theme/components/GettingStartedNextSteps.vue'
</script>

# Getting Started

Build your first SimpleW HTTP endpoint and see it running in just a few minutes.

<AddonMeta
    package="SimpleW"
    license="MIT"
    license-url="https://github.com/Stratdev3/SimpleW/blob/master/licence"
/>

::: tip What you will build
A small HTTP server listening on `localhost:2015` with one endpoint that returns a JSON response.
:::


## Before you start

You need the [.NET 8 SDK or later](https://dotnet.microsoft.com/download) and a terminal. The commands below work on Windows, macOS and Linux.

You can check the SDK installed on your machine with:

```sh
dotnet --version
```


## 1. Create the project

Start with a regular .NET console application. SimpleW does not require a special host or project type.

```sh
mkdir hello-simplew
cd hello-simplew
dotnet new console
dotnet add package SimpleW
```

Your project is ready. There is no additional dependency or configuration file to install.


## 2. Write your first server

Replace the content of `Program.cs` with the following code:

```csharp:line-numbers [Program.cs]
using System.Net;
using SimpleW;
using SimpleW.Observability;

Log.SetSink(Log.ConsoleWriteLine);

var server = new SimpleWServer(IPAddress.Loopback, 2015);

server.MapGet("/api/hello", static () => {
    return new { message = "Hello from SimpleW!" };
});

await server.RunAsync();
```

This is a complete SimpleW application: one server, one route and one call that keeps it running.


## 3. Start SimpleW

Run the application from the project directory:

```sh
dotnet run
```

You should see output similar to the terminal below. Timestamps will naturally be different on your machine.

<GettingStartedTerminal />

The process stays active while SimpleW is listening. Leave this terminal open for the next step.


## 4. Make your first request

Open a second terminal and call the endpoint:

::: code-group

```powershell [PowerShell]
Invoke-RestMethod http://localhost:2015/api/hello
```

```sh [Command Prompt, macOS or Linux]
curl http://localhost:2015/api/hello
```

:::

Or open [http://localhost:2015/api/hello](http://localhost:2015/api/hello) directly in your browser.

SimpleW serializes the returned object as JSON:

```json
{
  "message": "Hello from SimpleW!"
}
```

You have just created and served your first SimpleW endpoint.


## 5. Understand the code

| Code | What it does |
| --- | --- |
| `Log.SetSink(...)` | Sends SimpleW lifecycle messages to the console. |
| `SimpleWServer(...)` | Creates a server bound locally to port `2015`. |
| `MapGet(...)` | Maps `GET /api/hello` to a C# delegate. |
| `RunAsync()` | Starts listening and waits until the application is stopped. |

Press <kbd>Ctrl</kbd> + <kbd>C</kbd> in the first terminal when you want to stop the server.

::: details Prefer to start from a template?
SimpleW also provides project templates. See the [Templates addon](../addons/template-templates.md#simplew-minimal) when you want to skip the manual project setup.
:::


## Where to go next

Choose the path that matches what you want to build.

<GettingStartedNextSteps />
