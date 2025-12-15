using System;
using System.Net;
using NetCoreServer;
using Newtonsoft.Json;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddStaticContent(@"C:\www\client\", "/", TimeSpan.FromDays(1));

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