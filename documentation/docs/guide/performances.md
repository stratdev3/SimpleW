# Performances

Performance is a **built-in feature** !

Thanks to its [architecture](./what-is-simplew#architecture), SimpleW can handle the [10K connections problem](https://en.wikipedia.org/wiki/C10k_problem).


## Server Comparison

Monitoring requests-per-second is not relevant as it depend on the hardware. <br />
What matters is how each server performs relative to the others in the same conditions.

All tests are performed on Linux on a capable machine.


#### Evaluate request with a server containing a single route that returns a json serialized object

Command : [`bombardier -c 200 -d 30s http://127.0.0.1:8080/api/test/hello`](https://github.com/codesenberg/bombardier)

| Server                                                           | Perf (%)   | Total (req)     | Max (req/s)     | Average (req/s)     |
|------------------------------------------------------------------|-----------:|----------------:|----------------:|--------------------:|
| [ActixWeb](/snippets/perf-actixweb-dynamic-1.rs.txt)             |  **100**   |  **10.214.741**  |      438.269    |       **340.593**   |
| [Node](/snippets/perf-node-dynamic-1.js.txt)                     |     97     |     9.923.524    |    **456.048**  |         330.620     |
| [SimpleW](/snippets/perf-simplew-dynamic-1.cs.txt)               |     84     |     8.542.876    |      361.732    |         284.923     |
| [AspNetCore](/snippets/perf-aspnetcore-dynamic-1.cs.txt)         |     81     |     8.301.520    |      351.804    |         276.955     |
| [FastEndpoints](/snippets/perf-fastendpoints-dynamic-1.cs.txt)   |     78     |     7.935.007    |      335.625    |         264.463     |
| [FastHttp](/snippets/perf-fasthttp-dynamic-1.go.txt)             |     68     |     6.991.738    |      302.738    |         233.088     |
| [GenHttp](/snippets/perf-genhttp-dynamic-1.cs.txt)               |     66     |     6.765.910    |      304.603    |         225.697     |
| [Fastify](/snippets/perf-fastify-dynamic-1.js.txt)               |      8     |       821.255    |       27.397    |          33.817     |
| [EmbedIO](/snippets/perf-embedio-dynamic-1.cs.txt)               |      1     |       143.474    |       10.538    |           4.785     |

Node is untouchable. SimpleW, AspnetCore and FastEndpoints offer excellent performance.


<!--

#### Evaluate request with a server containing a single route and returning json object

### Static Files

Evaluate request on a single text file `message.txt` which contains `hello world`.

-->
