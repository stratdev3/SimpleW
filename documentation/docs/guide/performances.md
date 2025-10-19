# Performances

Performance is a **built-in feature** !

Thanks to its [architecture](./what-is-simplew#architecture), SimpleW can handle the [10K connections problem](https://en.wikipedia.org/wiki/C10k_problem).


## Server Comparison

Monitoring requests-per-second is not relevant as it depend on the hardware. <br />
What matters is how each server performs relative to the others in the same conditions.

All tests are performed on Linux on a capable machine.


#### Evaluate request with a server containing a single route that returns a json serialized object

Command : [`bombardier -c 200 -d 30s http://127.0.0.1:8080/api/test/hello`](https://github.com/codesenberg/bombardier)

| Server                                                                                                                                                                                                                                                                                                                            | Perf (%)   | Total (req)     | Max (req/s)     | Average (req/s)     |
|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------:|----------------:|----------------:|--------------------:|
| <a href="https://actix.rs" target="_blank" class="no-external-link-icon" title="site">ActixWeb</a> <a href="/SimpleW/snippets/perf-actixweb-dynamic-1.rs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                               |  **100**   |  **10.214.741**  |      438.269    |       **340.593**   |
| <a href="https://nodejs.org" target="_blank" class="no-external-link-icon" title="site">Node</a> <a href="/SimpleW/snippets/perf-node-dynamic-1.js.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                                     |     97     |     9.923.524    |    **456.048**  |         330.620     |
| <a href="https://stratdev3.github.io/SimpleW" target="_blank" class="no-external-link-icon" title="site">SimpleW</a> <a href="/SimpleW/snippets/perf-simplew-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                              |     84     |     8.542.876    |      361.732    |         284.923     |
| <a href="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel" target="_blank" class="no-external-link-icon" title="site">AspNetCore</a> <a href="/SimpleW/snippets/perf-aspnetcore-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a> |     81     |     8.301.520    |      351.804    |         276.955     |
| <a href="https://fast-endpoints.com" target="_blank" class="no-external-link-icon" title="site">FastEndpoints</a> <a href="/SimpleW/snippets/perf-fastendpoints-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                           |     78     |     7.935.007    |      335.625    |         264.463     |
| <a href="https://github.com/valyala/fasthttp" target="_blank" class="no-external-link-icon" title="site">FastHttp</a> <a href="/SimpleW/snippets/perf-fasthttp-dynamic-1.go.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                            |     68     |     6.991.738    |      302.738    |         233.088     |
| <a href="https://genhttp.org" target="_blank" class="no-external-link-icon" title="site">GenHttp</a> <a href="/SimpleW/snippets/perf-genhttp-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                              |     66     |     6.765.910    |      304.603    |         225.697     |
| <a href="https://fastify.dev" target="_blank" class="no-external-link-icon" title="site">Fastify</a> <a href="/SimpleW/snippets/perf-fastify-dynamic-1.js.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                                              |      8     |       821.255    |       27.397    |          33.817     |
| <a href="https://github.com/unosquare/embedio" target="_blank" class="no-external-link-icon" title="site">EmbedIO</a> <a href="/SimpleW/snippets/perf-embedio-dynamic-1.cs.txt" target="_blank" class="no-external-link-icon no-link-decoration" title="source code benchmark">📄</a>                                             |      1     |       143.474    |       10.538    |           4.785     |

Node is untouchable. SimpleW, AspnetCore and FastEndpoints offer excellent performance.


<!--

#### Evaluate request with a server containing a single route and returning json object

### Static Files

Evaluate request on a single text file `message.txt` which contains `hello world`.

-->

::: tip NOTE
The bombardier command is run three times, then keep the best result.
:::
