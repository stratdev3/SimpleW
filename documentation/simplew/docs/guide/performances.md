# Performances

Performance is a **built-in feature**, not an optional optimization.

Thanks to its [architecture](./what-is-simplew#architecture), SimpleW can handle the [10K connections problem](https://en.wikipedia.org/wiki/C10k_problem).


## Server Comparison

Monitoring requests-per-second is not relevant as it depend on the hardware. <br />
What matters is how each server performs relative to the others in the same conditions.

All tests are performed on Linux on a capable machine.


#### Evaluate request with a server containing a single route that returns a json serialized object

Command : [`bombardier -c 200 -d 30s http://127.0.0.1:8080/api/test/hello`](https://github.com/codesenberg/bombardier)

| Server                                                                                                                                                                                                                                                                                                                            | Perf (%)   | Total (req)     | Max (req/s)     | Average (req/s)    |
|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------:|----------------:|----------------:|-------------------:|
| <a href="https://actix.rs" target="_blank" class="no-external-link-icon" title="site">ActixWeb</a> <a href="/SimpleW/v26/snippets/perf-actixweb-dynamic-1.rs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                               |   **100**  |  **10.680.216**  |   **494.036**  |      **356.369**  |
| <a href="https://nodejs.org" target="_blank" class="no-external-link-icon" title="site">Node</a> <a href="/SimpleW/v26/snippets/perf-node-dynamic-1.js.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                                     |      94    |    10.080.386    |      464.080   |        335.814    |
| <a href="https://stratdev3.github.io/SimpleW" target="_blank" class="no-external-link-icon" title="site">SimpleW</a> <a href="/SimpleW/v26/snippets/perf-simplew-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                              |      90    |     9.620.831    |      458.121   |        320.991    |
| <a href="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel" target="_blank" class="no-external-link-icon" title="site">AspNetCore</a> <a href="/SimpleW/v26/snippets/perf-aspnetcore-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a> |      84    |     9.064.941    |      396.917   |        302.554    |
| <a href="https://fast-endpoints.com" target="_blank" class="no-external-link-icon" title="site">FastEndpoints</a> <a href="/SimpleW/v26/snippets/perf-fastendpoints-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                           |      83    |     8.887.823    |      403.860   |        296.483    |
| <a href="https://github.com/valyala/fasthttp" target="_blank" class="no-external-link-icon" title="site">FastHttp</a> <a href="/SimpleW/v26/snippets/perf-fasthttp-dynamic-1.go.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                            |      64    |     6.846.205    |      276.119   |        228.399    |
| <a href="https://genhttp.org" target="_blank" class="no-external-link-icon" title="site">GenHttp</a> <a href="/SimpleW/v26/snippets/perf-genhttp-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                              |      62    |     6.709.236    |      318.067   |        223.971    |
| <a href="https://gin-gonic.com/" target="_blank" class="no-external-link-icon" title="site">Gin-Gonic</a> <a href="/SimpleW/v26/snippets/perf-gingonic-dynamic-1.go.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                        |      11    |     1.255.802    |      53.615    |         41.869    |
| <a href="https://fastify.dev" target="_blank" class="no-external-link-icon" title="site">Fastify</a> <a href="/SimpleW/v26/snippets/perf-fastify-dynamic-1.js.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                              |       8    |       958.221    |       35.793   |         31.940    |
| <a href="https://github.com/unosquare/embedio" target="_blank" class="no-external-link-icon" title="site">EmbedIO</a> <a href="/SimpleW/v26/snippets/perf-embedio-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                             |       1    |       144.493    |       10.934   |          4.819    |


SimpleW is very close to Node. ActixWeb is still on top.


<!--

#### Evaluate request with a server containing a single route and returning json object

### Static Files

Evaluate request on a single text file `message.txt` which contains `hello world`.

-->

::: tip NOTE
The bombardier command is run three times, then keep the best result.
:::


## Performance-Oriented Configuration

To get the best performance, enable the following options :

```csharp:line-numbers
var server = new SimpleWServer(IPAddress.Any, 2015);
server.Configure(options => {
    // Always beneficial socket options
    options.TcpNoDelay = true;
    options.ReuseAddress = true;
    options.TcpKeepAlive = true;

    // Advanced tuning (platform dependent)
    options.AcceptPerCore = true;
    options.ReusePort = true; // linux only
});
```

These options improve :
- Connection acceptance scalability
- Latency under load
- CPU core utilization

## Common Performance Killers

Even a fast server can be made slow by misuse.

### Excessive Middleware

- Middleware runs on every request
- Each middleware adds overhead
- Complex middleware stacks multiply cost

Use middleware only for true cross-cutting concerns.

### Manual Request Parsing

Do **not** parse raw request data yourself.
- Use [`HttpRequest`](../reference/httprequest.md) helpers
- They are optimized and allocation-aware
- Custom parsing often introduces bugs and hidden costs

If something is missing, open an issue instead of reimplementing it.

### Console Output

```csharp
Console.WriteLine(...);
```

This is **extremely slow** under load.
- Introduces global locks
- Destroys throughput
- Skews benchmarks

Never use it in production hot paths.
