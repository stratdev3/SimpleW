# Unix Sockets

[Unix Domain Socket](https://en.wikipedia.org/wiki/Unix_domain_socket) (UDS) can also be used as an entrypoint for the server.
They are supported on : **Linux**, **MacOS**, **Android**... and even **Windows** !

With just a small change, the [basic api example](./api-basic) can also be served over a Unix socket.

::: code-group

<<< @/snippets/unix-sockets.cs#snippet{13 csharp:line-numbers} [program.cs]

:::

You can use `curl` to test :

```
$ curl --unix-socket C:\www\test.sock http://localhost/api/test
> { "message" : "Hello World !" }
```

There only one change :
- L13 : use the `simplew()` constructor with `UnixDomainSocketEndPoint` argument.

