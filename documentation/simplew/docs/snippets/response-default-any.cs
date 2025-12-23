using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static async Task Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all Controllers classes and serve on the "/api/" endpoint
            server.UseControllers<Controller>("/api");

            Console.WriteLine("server started at http://localhost:{server.Port}/");

            // start a blocking background server
            await server.RunAsync();
        }
    }

    public class TestController : Controller {

        [Route("GET", "/test1")]
        public object Test1() {
            // return: { "hello": "world", "date": "2023-10-23T00:00:00+02:00", "result": true }
            return new {
                hello = "world",
                date = new DateTime(2023, 10, 23),
                result = true
            };
        }

        [Route("GET", "/test2")]
        public object Test2() {
            // return: ["hello", "world"]
            return new string[] { "hello", "world" };
        }

    }

    public class UserController : Controller {

        [Route("GET", "/users")]
        public object Users() {
            // return: [{"Email":"user1@localhost","FullName":"user1"},{"Email":"user2@localhost","FullName":"user2"}]
            var users = new List<User>() {
                new User() { Email = "user1@localhost", FullName = "user1" },
                new User() { Email = "user2@localhost", FullName = "user2" },
            };
            return users;
        }

    }

    // example class
    public class User {
        // these public properties will be serialized
        public string Email { get; set; }
        public string FullName { get ;set; }
        // private will not be serialized
        private bool Enabled = false;
    }

}