using System;
using System.Net;
using Newtonsoft.Json;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddStaticContent(@"C:\www\client\", "/", TimeSpan.FromDays(1));

            // find all Controllers class and serve on the "/websocket/" endpoint
            server.AddWebSocketContent("/websocket");

            await server.RunAsync();
            Console.WriteLine("http server started at http://localhost:{server.Port}/");
            Console.WriteLine("websocket server started at ws://localhost:{server.Port}/websocket");

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