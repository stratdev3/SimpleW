# Response Types

## Default


The return of the `method` will be serialized to json using the excellent `JsonConvert.SerializeObject()` from [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/)

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {
        [Route("GET", "/test")]
        public object Test() {
            return new {
                message = "Hello World !",
                current = DateTime.Now,
                i = 0,
                enable = true,
                d = new Dictionary<string, string>() { { "Foo", "Bar" } }
            };
        }
    }

}
```

Requesting to `http://localhost:2015/api/test` will result to

```json
{
    "message": "Hello World !",
    "current": "2024-03-01T13:17:29.1249399+01:00",
    "i": 0,
    "enable": true,
    "d": {"Foo":"Bar"}
}
```


## Types

Any return type (`object`, `List`, `Dictionary`, `String`...) will be serialized and sent as json to the client.

The following example illustrates different return types :

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all Controllers classes and serve on the "/api/" endpoint
            server.AddDynamicContent("/api");

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

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
```

To see the results, open your browser to :
- http://localhost:2015/api/test1
- http://localhost:2015/api/test2
- http://localhost:2015/api/users

Note : there is no need to specify the exact type the method will return.
Most of the time, `object` is enough and will be passed to a `JsonConvert.SerializeObject(object)`.


## Helpers

In fact, the `Controller` class is dealing with an `HttpResponse` object which is sent async to the client.<br />
You can manipulate this object with the property `Response`.

There are also some useful helpers that facilitate returning specific `HttpReponse` :

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {

        [Route("GET", "/test1")]
        public object Test1() {
            // the object return will be serialized
            // and set as body of the HttpReponse
            // and a mimetype json
            // with a status code 200
            return new { hello = "world" };
        }

        [Route("GET", "/test2")]
        public object Test2() {
            try {
                throw new Exception("test2");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 500
                return MakeInternalServerErrorResponse(ex.Message);
            }
        }

        [Route("GET", "/test3")]
        public object Test3() {
            try {
                throw new KeyNotFoundException("test3");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 404
                return MakeNotFoundResponse(ex.Message);
            }
        }

        [Route("GET", "/test4")]
        public object Test4() {
            try {
                throw new UnauthorizedAccessException("test4");
            }
            catch (Exception ex) {
                // set message exception as body of the HttpReponse
                // and a mimetype text
                // with a status code 401
                return MakeUnAuthorizedResponse(ex.Message);
            }
        }

        [Route("GET", "/test5")]
        public object Test5() {
            var content = "download text content";
            // will force download a file "file.txt" with content
            return MakeDownloadResponse(content, "file.txt");
        }

    }

}
```

Note : all these helpers support different types of parameters and options to deal with
most of the use cases. Just browse to discover all the possibilities.
