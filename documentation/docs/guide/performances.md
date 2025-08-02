# Performances

Performance is a **built-in feature** !

Thanks to its [architecture](./what-is-simplew#architecture), SimpleW can handle the [10K connections problem](https://en.wikipedia.org/wiki/C10k_problem).


## Server Comparison

Monitoring requests-per-second is not relevant as it depend on the hardware. <br />
What matters is how each server performs relative to the others in the same conditions.


### Dynamic

#### Evaluate request with a server containing a single route and returning json object

Results average : 3 runs of [`bombardier -c 200 -d 30s $url`](https://github.com/codesenberg/bombardier)

| Server                                                   | Perf (%)   | Total (req)     | Max (req/s)     | Average (req/s)     | Min (req/s)     | Memory Footprint |
|----------------------------------------------------------|-----------:|----------------:|----------------:|--------------------:|----------------:|-----------------:|
| [SimpleW](/snippets/perf-simplew-dynamic-1.cs.txt)       |  **100**   |  **4.426.408**  |    **191.138**  |       **140.418**   |    **132.374**  |   81 Mo          |
| [AspNetCore](/snippets/perf-aspnetcore-dynamic-1.cs.txt) |  88        |    3.843.972    |      178.713    |         124.720     |      120.090    | **54 Mo**        |
| [GenHttp](/snippets/perf-genhttp-dynamic-1.cs.txt)       |  54        |    2.354.276    |      111.569    |          76.034     |       73.196    |  165 Mo          |
| [FastHttp](/snippets/perf-fasthttp-dynamic-1.go.txt)     |  52        |    2.205.770    |      140.447    |          73.408     |       73.324    |    ? Mo          |
| [EmbedIO](/snippets/perf-embedio-dynamic-1.cs.txt)       |   5        |      222.492    |       10.915    |           6.871     |       10.915    |  757 Mo          |

SimpleW delivers top performances with minimal memory footprint overhead compared to the ASP.NET Core.


<!--

#### Evaluate request with a server containing a single route and returning json object

### Static Files

Evaluate request on a single text file `message.txt` which contains `hello world`.

-->
