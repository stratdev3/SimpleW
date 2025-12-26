# Basic Auth

The **BasicAuthModule** provides lightweight HTTP **Basic Authentication** for
SimpleW applications.

It allows you to protect one or more **URL prefixes** with username/password
credentials, using a **single middleware in the pipeline**, no matter how many
times the module is configured.


## How It Works

Each call to [`SimpleWServer.UseBasicAuthModule()`](../reference/basicauthmodule.md) **adds or updates a rule** for a given URL
prefix.

Internally:
- All rules are stored in a shared registry
- Only **one middleware** is installed per `SimpleWServer`
- Incoming requests are matched against registered prefixes
- The **longest matching prefix** is selected
- Authentication is enforced only for that prefix


## Basic Usage

::: code-group

<<< @/snippets/basicauthmodule.cs#snippet{csharp:line-numbers} [program.cs]

:::

::: warning
- You should never expose BasicAuth over plain HTTP, but above HTTPS !
- Credentials are Base64-encoded, not encrypted.
:::