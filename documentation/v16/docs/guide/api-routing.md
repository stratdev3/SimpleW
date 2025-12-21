# Routing


Each URL is a concatenation of :
1. `Prefix` defined by a Component (e.g: [`AddDynamicContent()`](../reference/simplewserver#adddynamiccontent), [`AddStaticContent()`](../reference/simplewserver#addstaticcontent)).
2. [`Route`](../reference/routeattribute#controller) attribute on Controller class (if exists).
3. [`Route`](../reference/routeattribute#method) attribute on Method.


## The Route Attribut

`Route` attribute must be set to target the methods which will be called depending on url.

```csharp:line-numbers{20,26}
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

When all your methods share the same prefix, you can defined a `Route` attribute on controller class.
Thus, we can rewrite the previous example :

```csharp:line-numbers{17,21,27}
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

When a controller defines a route prefix, every method in that controller automatically inherits it.
However, by setting `isAbsolutePath: true` on a `Route` method, you can override this behavior and 
treat the specified URL as an absolute path.

```csharp:line-numbers{27}
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

    }

}
```

Methods can also have multiple `Route` attributes like in the following with the `Delete()` method
and its 2 differents routes.

```csharp:line-numbers{35,36}
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



## Regexp in Route

`Route` can support regular expressions when the server property `Router.RegExpEnabled` is true.

```csharp:line-numbers{12,27}
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

:::tip NOTE
The property `RegExpEnabled` is global to all controllers and must be set before any `AddDynamicContent()` call.
:::

## Query String Parameters

Query String parameters are also supported in a similar way.
The library will map query string parameter to the method parameter.

```csharp:line-numbers{25,36,45}
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
                return Response.MakeInternalServerErrorResponse(ex.Message);
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

```csharp:line-numbers{25,35}
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


## Catch All Routes

You can setup a maintenance page to catch all api call by using the wildcard in a `Route` attribute.

```csharp:line-numbers{32}
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // need by MaintenanceController wildcard route parameter
            server.Router.RegExpEnabled = true;
            // add the dedidacted controller
            server.AddDynamicContent(typeof(MaintenanceController), "/api");

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }

    // inherit from Controller to target a class
    public class MaintenanceController : Controller {

        // wildcard route parameter will call all string under root api
        [Route("GET", "/*")]
        public object Maintenance() {
            return Response.MakeErrorResponse(503, "Maintenance");
        }

    }

}
```


## Versioning Routes

Like everything, a REST API is subject to change. But no one want brutal breaking change instead, assure a certain compabitlity
level or deprecating period.

The **first public release of an API must be versionned**. For example :

```csharp:line-numbers{12}
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // api v1
            server.AddDynamicContent("/api/v1");

            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/index")]
        public object Index() {
            return "test index page";
        }

        [Route("POST", "/create")]
        public object Create() {
            return "test create success";
        }

    }

}
```

I’m planning to add new features to `TestController` that may introduce breaking changes, 
but i need to keep the previous version intact so existing clients can continue using it.

```csharp:line-numbers{12}
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // api v1
            server.AddDynamicContent(typeof(TestController), "/api/v1");

            // api v2
            server.AddDynamicContent(typeof(Test2Controller), "/api/v2");

            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/index")]
        public object Index() {
            return "test index page";
        }

        [Route("POST", "/update")]
        public object Update() {
            return "test update success";
        }

    }

    // the new Test controller
    [Route("/test")]
    public class Test2Controller : Controller {

        // the new version handle query string parameter and result json object
        [Route("GET", "/index")]
        public object Index(string filter=null, int page = 0) {
            return new {
                content = "test index page",
                count = 1234,
                page = 3
            };
        }

        // the new version handle query string parameter and result json object
        [Route("POST", "/update")]
        public object Update(Guid id) {
            return new {
                result = "success",
                message = "test update success"
            };
        }

    }

}
```