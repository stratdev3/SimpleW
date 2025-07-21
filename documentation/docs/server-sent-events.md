# Server Sent Events

Server-Sent Events (SSE) is a server push technology enabling a client to receive automatic updates from a server via an HTTP connection.

## Pushing data to all clients

This example illustrates how SimpleW can be used to :
1. serve an index.html static file which contains javascript code to connect to server sent events
2. serve a sse endpoint
3. response to all clients

Content of the `index.html` located in the `C:\www\client\` directory

```html:line-numbers
<html>
<head>
    <style>
    body {
        font-family: sans-serif;
        margin: 2rem;
    }
    #status {
        margin-bottom: 1rem;
        font-weight: bold;
    }
    #events {
        border: 1px solid #ccc;
        padding: 1rem;
        height: 300px;
        overflow-y: auto;
        background: #f9f9f9;
    }
    .event {
        margin-bottom: 0.5rem;
        padding: 0.3rem;
        border-bottom: 1px dashed #ddd;
    }
    .error {
        color: red;
    }
    </style>
    <script>
    let eventSource = null;
    const statusEl = document.getElementById('status');
    const eventsEl = document.getElementById('events');
    const btnConnect = document.getElementById('btnConnect');
    const btnDisconnect = document.getElementById('btnDisconnect');

    function connect() {
        if (eventSource) {
            return;
        }
        statusEl.textContent = '🟢 Status : connexion en cours…';
        eventSource = new EventSource('/api/test/sse');
        eventSource.onopen = () => {
            statusEl.textContent = '🟢 Status : connected';
            btnConnect.disabled = true;
            btnDisconnect.disabled = false;
        };
        eventSource.onmessage = event => {
            const div = document.createElement('div');
            div.className = 'event';
            div.textContent = event.data;
            eventsEl.appendChild(div);
            eventsEl.scrollTop = eventsEl.scrollHeight;
        };
        eventSource.onerror = err => {
            statusEl.textContent = '🔴 Status : connection error';
            statusEl.classList.add('error');
            console.error('SSE error:', err);
        };
    }

    function disconnect() {
        if (!eventSource) {
            return;
        }
        eventSource.close();
        eventSource = null;
        statusEl.textContent = '🟡 Status : disconnected';
        btnConnect.disabled = false;
        btnDisconnect.disabled = true;
    }

    // buttons click
    btnConnect.addEventListener('click', connect);
    btnDisconnect.addEventListener('click', disconnect);
    </script>
</head>
<body>
    <h1>Client SSE NetCoreServer</h1>
    <div id="status">🟡 Status : waiting connection…</div>
    <button id="btnConnect">🔌 Connect</button>
    <button id="btnDisconnect" disabled>❌ Disconnect</button>
    <div id="events"></div>
</body>
</html>
```

Use `server.AddDynamicContent()` to handle SSE endpoint.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {

        static void Main(string[] args) {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directory which contains the index.html
            server.AddStaticContent(@"C:\www\client", "/");

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("sse server started at ws://localhost:2015/api/test/sse");

            // menu
            while (true) {
                Console.WriteLine("\nMenu : (S)end or (Q)uit ?\n");
                var key = Console.ReadKey().KeyChar.ToString().ToLower();
                if (key == "q") {
                    Environment.Exit(0);
                }
                if (key == "s") {
                    // multicast message to all connected sessions
                    Console.WriteLine($"\nsend hello to all clients\n");
                    server.BroadcastSSESessions("message", "hello");
                }
            }

        }
    }

    [Route("/test")]
    public class TestController : Controller {

        // SSE session is initiating with a GET method from client
        [Route("GET", "/sse")]
        public object SSE() {
            // elevate the current session as a SSE Sessions
            AddSSESession();
            // return SSE stream response to client
            return MakeServerSentEventsResponse();
        }

    }

}

```

Open your browser to `http://localhost:2015/` :
- your browser will connect to SSE endpoint and show logs connections
- press the `connect` button in the html page
- press `s` key from the server console to send a SSE message to all clients.
- see logs in both side.

Note : the `server.BroadcastSSESessions()` will send response to all SSE clients.


## Pushing data to specifics clients

In the following example, the SSE is only enabled for authenticated user.
Then the broadcast will target only administrator profils.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {

        static void Main(string[] args) {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directory which contains the index.html
            server.AddStaticContent(@"C:\www\client", "/");

            // find all Controllers classes and serve on the "/api" endpoint
            server.AddDynamicContent("/api");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("sse server started at ws://localhost:2015/api/test/sse");

            // menu
            while (true) {
                Console.WriteLine("\nMenu : (S)end or (Q)uit ?\n");
                var key = Console.ReadKey().KeyChar.ToString().ToLower();
                if (key == "q") {
                    Environment.Exit(0);
                }
                if (key == "s") {
                    Console.WriteLine($"\nsend hello to all clients\n");
                    // multicast message to all administrator
                    server.BroadcastSSESessions("message", "hello", (session) => session.webuser.Profile == "Administrator");
                }
            }

        }
    }

    [Route("/test")]
    public class TestController : Controller {

        // SSE session is initiating with a GET method from client
        [Route("GET", "/sse")]
        public object SSE() {

            // sse only for authenticated user
            if (webuser.Id == Guid.Empty) {
               return MakeAccessResponse();
            }

            // elevate the current session as a SSE Sessions
            AddSSESession();
            // return SSE stream response to client
            return MakeServerSentEventsResponse();
        }

    }

}

```