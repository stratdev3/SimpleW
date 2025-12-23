# Server Sent Events [⚠️ need update to v26]

Server-Sent Events (SSE) is a server push technology enabling a client to receive automatic updates from a server via an HTTP connection.

## Pushing data to all clients

This example illustrates how SimpleW can be used to :
1. serve an index.html static file which contains javascript code to connect to server sent events
2. serve a sse endpoint with `server.MapControllers<Controller>()`
3. response to all clients

<br />

::: code-group

<<< @/snippets/sse.cs#snippet{csharp:line-numbers} [program.cs]

<<< @/snippets/sse.html#snippet{html:line-numbers} [C:\www\client\index.html]

:::


Open your browser to `http://localhost:2015/` :
- your browser will connect to SSE endpoint and show logs connections
- press the `connect` button in the html page
- press `s` key from the server console to send a SSE message to all clients.
- see logs in both side.

Note : the `server.BroadcastSSESessions()` will send response to all SSE clients.


## Pushing data to specifics clients

In the following example, the SSE is only enabled for authenticated user.
Then the broadcast will target only administrator profils.

::: code-group

<<< @/snippets/sse-pushing-data.cs#snippet{csharp:line-numbers} [program.cs]

:::