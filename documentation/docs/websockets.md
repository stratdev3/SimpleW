# WebSockets


The advantage of Websockets over HTTP is the two-way communication channels : server can push data to the client without it has to request (except first time to connect socket).

More clearly : websocket avoid client polling request to server to get fresh data.

## Pushing data to all clients

This example illustrates how SimpleW can be used to :
1. serve an index.html static file which contains javascript code to connect to websocket
2. serve a websocket endpoint
3. response to all clients

Content of the `index.html` located in the `C:\www\client\` directory

```html
<html>
<head>
    <script>
        document.addEventListener("DOMContentLoaded", function(event) {
            // logging
            function logs(message, color) {
                let logs = document.querySelector('#logs');
                let log = document.createElement('li');
                log.textContent = message;
                log.style.color = color;
                logs.append(log);
            }
            // websocket client
            var ws = new WebSocket('ws://localhost:2015/websocket');
            ws.onopen = function(e) {
                logs('[connected] connection established to server.', 'green');
                logs('// you can press S key from the server console.', 'blue');
            };
            ws.onmessage = function(event) {
                logs(`[message] Data received from server: ${event.data}`, 'green');
            };
            ws.onclose = function(event) {
                logs('[close] connection ' + (event.wasClean ? 'closed cleanly' : 'died'), 'red');
            };
            ws.onerror = function(error) {
                logs(`[error] ${error}`, 'red');
            };
        });
    </script>
</head>
<body>
    <h1>Example Websocket Client</h1>
    <ol id="logs"></ol>
</body>
</html>
```

Use `server.AddWebSocketContent()` to handle WebSocket endpoint.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directory which contains the index.html
            server.AddStaticContent(@"C:\www\client", "/");

            // find all Controllers class and serve on the "/websocket/" endpoint
            server.AddWebSocketContent("/websocket");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("websocket server started at ws://localhost:2015/websocket");

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
                    server.MulticastText("hello");
                }
            }

        }
    }

}
```

Open your browser to `http://localhost:2015/` :
- your browser will connect to websocket and show logs connections
- press `s` key from the server console to send a websocket message to all clients.
- see logs in both side.

Note : the `server.MulticastText()` will send response to all websocket clients.


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
2. serve a websocket endpoint
3. receive data from client
4. response to client


### Client

Content of the `index.html` located in the `C:\www\client\` directory

```html
<html>
<head>
    <script>
        document.addEventListener("DOMContentLoaded", function(event) {
            // logging
            function logs(message, color) {
                let logs = document.querySelector('#logs');
                let log = document.createElement('li');
                log.textContent = message;
                log.style.color = color;
                logs.append(log);
            }
            // websocket client
            var ws = new WebSocket('ws://localhost:2015/websocket');
            ws.onopen = function(e) {
                logs('[connected] connection established to server.', 'green');
                document.getElementById('send1').style = 'display: inline;';
                document.getElementById("send1").addEventListener('click', SendWebsocketData1);
                document.getElementById('send2').style = 'display: inline;';
                document.getElementById("send2").addEventListener('click', SendWebsocketData2);
                logs('// you can click on "send data 1" or "send data 2" buttons.', 'blue');
            };
            ws.onmessage = function(event) {
                logs(`[message] Data received from server: ${event.data}`, 'green');
            };
            ws.onclose = function(event) {
                logs('[close] connection ' + (event.wasClean ? 'closed cleanly' : 'died'), 'red');
            };
            ws.onerror = function(error) {
                logs(`[error] ${error}`, 'red');
            };
            // buttons click
            function SendWebsocketData1() {
                var message = { url: "/websocket/test/index" };
                ws.send(JSON.stringify(message));
                logs('// you click send { url: "/websocket/test/index" } to websocket server.', 'blue');
            };
            function SendWebsocketData2() {
                var message = { url: "/websocket/test/create" };
                ws.send(JSON.stringify(message));
                logs('// you click send { url: "/websocket/test/create" } to websocket server.', 'blue');
            };
        });
    </script>
</head>
<body>
    <h1>Example Websocket Client</h1>
    <button style="display: none" id="send1">send test/index</button>
    <button style="display: none" id="send2">send test/create</button>
    <ol id="logs"></ol>
</body>
</html>
```

### Server

Use `server.AddWebSocketContent()` to declare Controllers to a WebSocket endpoint.

The target method need to have a uniq parameter of type `WebSocketMessage` and Route Attribute must have `"WEBSOCKET"` as HTTP Verb.

```csharp:line-numbers
using System;
using System.Net;
using NetCoreServer;
using Newtonsoft.Json;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddStaticContent(@"C:\www\client\", "/");

            // find all Controllers class and serve on the "/websocket/" endpoint
            server.AddWebSocketContent("/websocket");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("websocket server started at ws://localhost:2015/websocket");
            Console.ReadKey();

        }
    }

    [Route("/test")]
    public class TestController : Controller {

        // call by websocket with websocketMessage url = "/websocket/test/index"
        [Route("WEBSOCKET", "/index")]
        public void Index(WebSocketMessage message) {
            Console.WriteLine("receive message");

            // response to the client
            Session.SendText("index");
        }

        // call by websocket with websocketMessage url = "/websocket/test/create"
        [Route("WEBSOCKET", "/create")]
        public void Create(WebSocketMessage message) {
            Console.WriteLine("receive message");

            // response to the client
            Session.SendText("index");
        }

    }

}
```

Open your browser to `http://localhost:2015/` :
- your browser will connect to websocket and show logs connections
- click the two buttons from the browser to send a websocket message to server.
- see logs in both side.

Note : use `Session.SendText()` will response to the websocket client.



## Advanced Communication between client/server

The following example shows how to pass custom data to the server using the `WebSocketMessage.body` property.

Frontend

```html
<html>
<head>
    <script>
        document.addEventListener("DOMContentLoaded", function(event) {
            // logging
            function logs(message, color) {
                let logs = document.querySelector('#logs');
                let log = document.createElement('li');
                log.textContent = message;
                log.style.color = color;
                logs.append(log);
            }
            // websocket client
            var ws = new WebSocket('ws://localhost:2015/websocket');
            ws.onopen = function(e) {
                logs('[connected] connection established to server.', 'green');
                document.getElementById('send1').style = 'display: inline;';
                document.getElementById("send1").addEventListener('click', SendWebsocketData1);
                document.getElementById('send2').style = 'display: inline;';
                document.getElementById("send2").addEventListener('click', SendWebsocketData2);
                logs('// you can click on "send data 1" or "send data 2" buttons.', 'blue');
            };
            ws.onmessage = function(event) {
                logs(`[message] Data received from server: ${event.data}`, 'green');
            };
            ws.onclose = function(event) {
                logs('[close] connection ' + (event.wasClean ? 'closed cleanly' : 'died'), 'red');
            };
            ws.onerror = function(error) {
                logs(`[error] ${error}`, 'red');
            };
            // buttons click
            function SendWebsocketData1() {
                var message = { url: "/websocket/user/index", body: "hello" };
                ws.send(JSON.stringify(message));
                logs('// you click send { url: "/websocket/user/index" } to websocket server.', 'blue');
            };
            function SendWebsocketData2() {
                var message = { url: "/websocket/user/create", body: "{ name: 'John Doe' enabled: 'true' }" };
                ws.send(JSON.stringify(message));
                logs('// you click send { url: "/websocket/user/create" } to websocket server.', 'blue');
            };
        });
    </script>
</head>
<body>
    <h1>Example Websocket Client</h1>
    <button style="display: none" id="send1">send user/index</button>
    <button style="display: none" id="send2">send user/create</button>
    <ol id="logs"></ol>
</body>
</html>
```

Backend receive

```csharp
using System;
using System.Net;
using NetCoreServer;
using Newtonsoft.Json;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddStaticContent(@"C:\www\client\", "/");

            // find all Controllers class and serve on the "/websocket/" endpoint
            server.AddWebSocketContent("/websocket");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("websocket server started at ws://localhost:2015/websocket");
            Console.ReadKey();

        }
    }

    [Route("/user")]
    public class UserController : Controller {

        // call by websocket with websocketMessage url = "/websocket/user/index"
        [Route("WEBSOCKET", "/index")]
        public void Index(WebSocketMessage message) {
            Console.WriteLine($"receive message {message.body}");

            // json response to the client
            Session.SendText(JsonConvert.SerializeObject(new { hello = "world" }));
        }

        // call by websocket with websocketMessage url = "/websocket/user/create"
        [Route("WEBSOCKET", "/create")]
        public void Create(WebSocketMessage message) {
            Console.WriteLine("receive message");

            var user = new User();
            
            NetCoreServerExtension.JsonMap(message.body.ToString(), user);

            user.id = Guid.NewGuid();
            user.creation = DateTime.Now;

            // json response to the client
            Session.SendText(JsonConvert.SerializeObject(user));
        }

    }

    public class User {
        public Guid id;
        public string name;
        public DateTime creation;
        public bool enabled;
    }

}
```

Note:
- `NetCoreServerExtension.JsonMap()` is a mapping helper utility similar to `BodyMap()` for RestAPI in the previous chapter.
