# Routes


Each route is a concatenation of :
1. `Prefix` defined by `AddDynamicContent()`.
2. `Route` attribute on Controller class (if exists).
3. `Route` attribute on Method.


## Examples

`Route` attribute on methods.

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

        // call on GET http://localhost:2015/api/test/index
        [Route("GET", "/test/index")]
        public object Index() {
            return "test index page";
        }

        // call POST http://localhost:2015/api/test/create
        [Route("POST", "/test/create")]
        public object Create() {
            return "test create success";
        }

    }

}
```

The same example can be refactored with `Route` attribute on controller class.

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

    [Route("/test")]
    public class TestController : Controller {

        // call on GET http://localhost:2015/api/test/index
        [Route("GET", "/index")]
        public object Index() {
            return "test index page";
        }

        // call POST http://localhost:2015/api/test/create
        [Route("POST", "/create")]
        public object Create() {
            return "test create success";
        }

    }

}
```

You can override a path with `isAbsolutePath: true` parameter.

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

    [Route("test/")]
    public class TestController : Controller {

        // test with http://localhost:2015/api/test/index
        [Route("GET", "/index")]
        public object Index() {
            return "test index page";
        }

        // test with http://localhost:2015/home
        [Route("GET", "/home", isAbsolutePath: true)]
        public object Home() {
            return "home page";
        }

        // test with POST http://localhost:2015/api/test/create
        [Route("POST", "/create")]
        public object Create() {
            return "test create success";
        }

        // test with POST http://localhost:2015/api/test/delete
        // or
        // test with POST http://localhost:2015/api/test/remove
        [Route("POST", "/delete")]
        [Route("POST", "/remove")]
        public object Delete() {
            return "test delete success";
        }

    }

}
```

Note :
- the `isAbsolutePath` flag will not take the prefix defined in the `AddDynamicContent`.
- methods can have multiple `Route` attributes (example above with _delete_, _remove_).


## Regexp

`Route` path support regular expressions when `Router.RegExpEnabled` is true.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // allow regular expression in route path
            server.Router.RegExpEnabled = true;

            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("/test")]
    public class TestController : Controller {

        // http://localhost:2015/api/test/index
        // or
        // http://localhost:2015/api/test/indexes
        [Route("GET", "/(index|indexes)")]
        public object Index() {
            return "test index page";
        }

    }

}
```

Note : the property `RegExpEnabled` is global to all controllers and must be
       set before any `AddDynamicContent()` call.


## Query String Parameters

Query String parameters are also supported in a similar way.
The library will map query string parameter to the method parameter.

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

        // test with http://localhost:2015/api/hello
        // test with http://localhost:2015/api/hello?name=stratdev
        //
        // parameter "name" has default value "world" 
        // so the query string "name" is not mandatory
        [Route("GET", "/hello")]
        public object Hello(string name = "world") {
            return $"Hello {name} !";
        }

        // test with http://localhost:2015/api/hi?name=stratdev
        // test with http://localhost:2015/api/hi
        //
        // parameter "name" has no default value
        // so the query string "name" is required
        // not providing it will return an HTTP 404 ERROR
        [Route("GET", "/hi")]
        public object Hi(string name) {
            return $"Hi {name} !";
        }

        // test with http://localhost:2015/api/bye?name=stratdev&exit=0
        //
        // it does not matter if there are others query strings 
        // than the one declared in the method
        [Route("GET", "/bye")]
        public object Bye(string name) {
            return $"Bye {name} !";
        }

        // test with http://localhost:2015/api/debug?a=bbbb&c=dddd
        [Route("GET", "/debug")]
        public object Debug() {
            try {
                // get NameValueCollection
                var nvc = Route.ParseQueryString(this.Request.Url);
                // convert to dictionnary
                var querystrings = nvc.AllKeys.ToDictionary(k => k, k => nvc[k]);
                return new {
                    message = $"list query string parameters from {this.Request.Url}",
                    querystrings
                };
            }
            catch (Exception ex) {
                return MakeInternalServerErrorResponse(ex.Message);
            }
        }

    }

}
```

Notes :
- query string are mapped by name to the parameter method.
- Only declared parameters are mapped (no exception when missing properties).
- When a method has a mandatory parameter (without default value), the route will not match if not provided in the url (return HTTP CODE 404).
- `Route.ParseQueryString(this.Request.Url)` to get all query strings


## Path Parameters

`Route` path parameters are also supported in a similar way.
When a `{parameter}` is declared in the path, it's possible to set parameters in `Route` path and retrieve their value in the method.<br />
The library will map them according to their names.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // allow regular expression in route path
            server.Router.RegExpEnabled = true;

            server.AddDynamicContent("/api");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("/test")]
    public class TestController : Controller {

        // test with http://localhost:2015/api/test/user/stratdev
        [Route("GET", "/user/{login}")]
        public object User(string login) {
            return $"Hello {login}";
        }

        // test with http://localhost:2015/api/test/user/stratdev/2023
        // but
        // test with http://localhost:2015/api/test/user/stratdev/xx will
        // return a http code 500 as the "xx" cast to integer
        // will throw an exception
        [Route("GET", "/user/{login}/{year}")]
        public object User(string login, int year) {
            return $"Hello {login}, you're {year} year old.";
        }

    }

}
```

Note :
- In this example, the value `stratdev` of `{login}` parameter will be mapped to `string login`
  and the value `2023` of `{year}` parameter will be mapped to `int year`.
- the string value of parameter will be cast to the parameter type.
  If the cast fails, an HTTP CODE 500 will be returned to the client.
- all declared parameters in `Route` path are mandatory.
