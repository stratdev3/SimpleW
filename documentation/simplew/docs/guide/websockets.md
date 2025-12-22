# WebSockets


The advantage of Websockets over HTTP is the two-way communication channels : server can push data to the client without it has to request (except first time to connect socket).

More clearly : websocket avoid client polling request to server to get fresh data.

## Pushing data to all clients

This example illustrates how SimpleW can be used to :
1. serve an index.html static file which contains javascript code to connect to websocket
2. serve a websocket endpoint with [`server.AddWebSocketContent()`](../reference/simplewserver#addwebsocketcontent)
3. response to all clients

<br />

::: code-group

<<< @/snippets/websockets.cs#snippet{csharp:line-numbers} [program.cs]

<<< @/snippets/websockets.html#snippet{html:line-numbers} [C:\www\client\index.html]

:::

Open your browser to `http://localhost:2015/` :
- your browser will connect to websocket and show logs connections
- press `s` key from the server console to send a websocket message to all clients.
- see logs in both side.

Note : the [`server.MulticastText()`](../reference/simplewserver#multicasttext) will send response to all websocket clients.


## Receiving data from client

SimpleW has its own way of handling websocket data from client. It will reuse the same logic as the RestAPI with `Controller`, `Route` and `Method`.

For this to work, the client has to pass a specific json structure, called `WebSocketMessage` to the websocket server.

```json
// WebSocketMessage
{
    // url is a mandatory property use to route message to the correct controller/method. it acts like a relative path from the websocket endpoint.
    "url": "",
    // optionnal property to pass data to controller
    "body": null,
}
```

This example illustrates how SimpleW can be used to :
1. serve an index.html static file which contains javascript code to connect to websocket
2. serve a websocket endpoint with [`server.AddWebSocketContent()`](../reference/simplewserver#addwebsocketcontent). The target method need to have a uniq parameter of type `WebSocketMessage` and Route Attribute must have `"WEBSOCKET"` as HTTP Verb.
3. receive data from client
4. response to client

<br />

::: code-group

<<< @/snippets/websockets-receiving.cs#snippet{csharp:line-numbers} [program.cs]

<<< @/snippets/websockets-receiving.html#snippet{html:line-numbers} [C:\www\client\index.html]

:::

Open your browser to `http://localhost:2015/` :
- your browser will connect to websocket and show logs connections
- click the two buttons from the browser to send a websocket message to server.
- see logs in both side.

Note : use `Session.SendText()` will response to the websocket client.


## Advanced Communication between client/server

The following example shows how to pass custom data to the server using the `WebSocketMessage.body` property.

::: code-group

<<< @/snippets/websockets-advanced.cs#snippet{csharp:line-numbers} [program.cs]

<<< @/snippets/websockets-advanced.html#snippet{html:line-numbers} [C:\www\client\index.html]

:::
