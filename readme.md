# SimpleW
[![NuGet Package](https://img.shields.io/nuget/v/SimpleW)](https://www.nuget.org/packages/SimpleW)
![NuGet Downloads](https://img.shields.io/nuget/dt/SimpleW)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](licence)
<br/>
[![Linux](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-linux.yml)
[![MacOS](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-macos.yml)
[![Windows (Visual Studio)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml/badge.svg)](https://github.com/stratdev3/SimpleW/actions/workflows/build-windows.yml)

<img src="src/SimpleW/logo.svg" alt="logo" width="100" />

SimpleW is a Simple Web server library in .NET (windows/linux/macos).<br />
It provides a cross-plateform framework for building web applications on top of the great [NetCoreServer](https://github.com/chronoxor/NetCoreServer) socket server.

### Features

1. Routing
2. Static Files
3. RestAPI (Controller/Method + automatic json serialization/deserialization)
4. Json Web Token
5. Websocket
6. OpenTelemetry

SimpleW is lightweight, easy to integrate, fast, minimal footprint, written in pure C# 100% managed, and only one dependency _(Newtonsoft.Json, will be remove in futur release)_.

If you wonder [why i wrote this library](#why-i-wrote-this-library) and what are [my needs](#so-my-needs).

Then, jump to the summary and see if it could fit yours. 

# Summary

- [Installation](#installation)
- [Usage](#usage)
  - [Routes](#routes)
  - [Serve Statics Files](#serve-statics-files)
    - [Basic Static Example](#basic-static-example)
    - [Multiples Directories](#multiples-directories)
    - [Options](#options)
    - [Cache](#cache)
  - [Serve RestAPI](#serve-restapi)
    - [Basic RestAPI Example](#basic-restapi-example)
    - [Return Type](#return-type)
    - [Return Helpers](#return-helpers)
    - [Routing](#routing)
      - [Examples](#examples)
      - [Regexp](#regexp)
      - [Query String Parameters](#query-string-parameters)
      - [Path Parameters](#path-parameters)
    - [Post Body](#post-body)
      - [Post Body (application/json) deserialization helper](#post-body-applicationjson-deserialization-helper)
      - [Post Body (application/x-www-form-urlencoded) deserialization helper](#post-body-applicationx-www-form-urlencoded-deserialization-helper)
      - [Post Body (multipart/form-data) deserialization helper](#post-body-multipartform-data-deserialization-helper)
    - [CORS](#cors)
    - [Serialization](#serialization)
    - [Hooks](#hooks)
      - [OnBeforeMethod](#onbeforemethod)
      - [Subclass](#subclass)
    - [Properties](#properties)
  - [JWT Authentication](#jwt-authentication)
    - [Get JWT](#get-jwt)
    - [Verify JWT](#verify-jwt)
    - [Forge JWT](#forge-jwt)
  - [Websockets](#websockets)
    - [Example Server pushing data to all clients](#example-server-pushing-data-to-all-clients)
    - [Example Server receiving data from client](#example-server-receiving-data-from-client)
  - [OpenTelemetry](#opentelemetry)


### Installation

Using the [SimpleW](https://www.nuget.org/packages/SimpleW) nuget package, always prefer the last [version](release.md).

```
dotnet add package SimpleW
```

Note : SimpleW depends on [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) package for json serialization/deserialization.
It will be replaced in futur by the native `System.Text.Json` as long as
some advanced features will be covered (`Populate` and `streamingContextObject`, see [work-in-progress](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/migrate-from-newtonsoft?pivots=dotnet-8-0#table-of-differences)).


# Usage


## Routes

In SimpleW, all is about **routing** and there are 2 different kinds of routes :
* **statics** : for serving statics files _(html, js, css, png...)_
* **dynamics** : for serving API _(C# code)_

Note : `Reflection` is only used to list routes, __once__, before server start.
An `expression tree` is built to call method fast __without__ using any slow `T.GetMethod().Invoke()`.


## Serve Statics Files


### Basic Static Example

To serve statics files with very few lines of code :

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve static content located in your folder "C:\www\spa\" to "/" endpoint
            server.AddStaticContent(@"C:\www\spa\", "/");

            // enable autoindex if no index.html exists in the directory
            server.AutoIndex = true;

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}
```

Then just point your browser to http://localhost:2015/.

Note : if `AutoIndex` is false and the directory does not contain a default document `index.html`, an http 404 error will return.

Note : on Windows, the Firewall can block this simple console app even if exposed on localhost and port > 1024. You need to allow access otherwise you will not reach the server.

### Multiples Directories

SimpleW can handle multiple directories as soon as they are declared under different endpoints.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // serve directories/endpoints
            server.AddStaticContent(@"C:\www\frontend", "/");
            server.AddStaticContent(@"C:\www\public", "/public/");

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/");

            // block console for debug
            Console.ReadKey();

        }
    }
}
```


### Options

You can change some settings before server start.

To change the default document
```csharp
server.DefaultDocument = "maintenance.html";
```

To add custom mime types

```csharp
server.AddMimeTypes(".vue", "text/html");
```


### Cache

The `AddStaticContent()` caches all directories/files in RAM (default: 1 hour) on server start.<br />
Also, an internal filesystem watcher is keeping this cache up-to-date.
It supports realtime file editing even when specific lock/write occurs.

To modify cache duration or to filter files

```csharp
// serve statics files
server.AddStaticContent(
    @"C:\www\",             // under C:\www or its subdirectories
    "/",                    // to / endpoint
    "*.csv",                // only CSV files
    TimeSpan.FromDays(1)    // set cache to 1 day
);
```


## Serve RestAPI

### Basic RestAPI Example

The RestAPI is based on **routes**, so just add a `RouteAttribute` to target **methods** of a `Controller` base class.<br />
The return is serialized into json and sent as a response to the client.

Use `server.AddDynamicContent()` to handle RestAPI.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all Controllers classes and serve on the "/api/" endpoint
            server.AddDynamicContent("/api/");

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/api/");

            // block console for debug
            Console.ReadKey();

        }
    }

    // inherit from Controller to target a class
    public class SomeController : Controller {

        // use the Route attribute to target a public method
        [Route("GET", "test")]
        public object SomePublicMethod() {
            // the return will be serialized to json
            return new {
                hello = "world"
            };
        }

    }

}
```

Then just open your browser to http://localhost:2015/api/test and you will see the `{ "hello": "world" }` json response.

Note : the controller __CAN NOT__ have constructor.


### Return Type

Any return type (`object`, `List`, `Dictionary`, `String`...) will be serialized and sent as json to the client.

The following example illustrates different return types :

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // listen to all IPs port 2015
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // find all Controllers classes and serve on the "/api/" endpoint
            server.AddDynamicContent("/api/");

            server.Start();

            Console.WriteLine("server started at http://localhost:2015/api/");

            // block console for debug
            Console.ReadKey();

        }
    }

    public class TestController : Controller {

        [Route("GET", "test1")]
        public object Test1() {
            // return: { "hello": "world", "date": "2023-10-23T00:00:00+02:00", "result": true }
            return new {
                hello = "world",
                date = new DateTime(2023, 10, 23),
                result = true
            };
        }

        [Route("GET", "test2")]
        public object Test2() {
            // return: ["hello", "world"]
            return new string[] { "hello", "world" };
        }

    }

    public class UserController : Controller {

        [Route("GET", "users")]
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


### Return Helpers

In fact, the `Controller` class is dealing with an `HttpResponse` object which is sent async to the client.<br />
You can manipulate this object with the property `Response`.

There are also some useful helpers that facilitate returning specific `HttpReponse` :

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {

        [Route("GET", "test1")]
        public object Test1() {
            // the object return will be serialized
            // and set as body of the HttpReponse
            // and a mimetype json
            // with a status code 200
            return new { hello = "world" };
        }

        [Route("GET", "test2")]
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

        [Route("GET", "test3")]
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

        [Route("GET", "test4")]
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

        [Route("GET", "test5")]
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


### Routing

Each route is a concatenation of :
1. `Prefix` defined by `AddDynamicContent()`.
2. `Route` attribute on Controller class (if exists).
3. `Route` attribute on Method.


#### Examples

`Route` attribute on methods.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {

        // call on GET http://localhost:2015/api/test/index
        [Route("GET", "test/index")]
        public object Index() {
            return "test index page";
        }

        // call POST http://localhost:2015/api/test/create
        [Route("POST", "test/create")]
        public object Create() {
            return "test create success";
        }

    }

}
```

The same example can be refactored with `Route` attribute on controller class.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("test/")]
    public class TestController : Controller {

        // call on GET http://localhost:2015/api/test/index
        [Route("GET", "index")]
        public object Index() {
            return "test index page";
        }

        // call POST http://localhost:2015/api/test/create
        [Route("POST", "create")]
        public object Create() {
            return "test create success";
        }

    }

}
```

You can override a path with `isAbsolutePath: true` parameter.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("test/")]
    public class TestController : Controller {

        // test with http://localhost:2015/api/test/index
        [Route("GET", "index")]
        public object Index() {
            return "test index page";
        }

        // test with http://localhost:2015/home
        [Route("GET", "/home", isAbsolutePath: true)]
        public object Home() {
            return "home page";
        }

        // test with POST http://localhost:2015/api/test/create
        [Route("POST", "create")]
        public object Create() {
            return "test create success";
        }

        // test with POST http://localhost:2015/api/test/delete
        // or
        // test with POST http://localhost:2015/api/test/remove
        [Route("POST", "delete")]
        [Route("POST", "remove")]
        public object Delete() {
            return "test delete success";
        }

    }

}
```

Note :
- the `isAbsolutePath` flag will not take the prefix defined in the `AddDynamicContent`.
- methods can have multiple `Route` attributes (example above with _delete_, _remove_).


#### Regexp

`Route` path support regular expressions when `Router.RegExpEnabled` is true.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // allow regular expression in route path
            server.Router.RegExpEnabled = true;

            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("test/")]
    public class TestController : Controller {

        // http://localhost:2015/api/test/index
        // or
        // http://localhost:2015/api/test/indexes
        [Route("GET", "(index|indexes)")]
        public object Index() {
            return "test index page";
        }

    }

}
```

Note : the property `RegExpEnabled` is global to all controllers and must be
       set before any `AddDynamicContent()` call.


#### Query String Parameters

Query String parameters are also supported in a similar way.
The library will map query string parameter to the method parameter.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
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
        [Route("GET", "hello")]
        public object Hello(string name = "world") {
            return $"Hello {name} !";
        }

        // test with http://localhost:2015/api/hi?name=stratdev
        // test with http://localhost:2015/api/hi
        //
        // parameter "name" has no default value
        // so the query string "name" is required
        // not providing it will return an HTTP 404 ERROR
        [Route("GET", "hi")]
        public object Hi(string name) {
            return $"Hi {name} !";
        }

        // test with http://localhost:2015/api/bye?name=stratdev&exit=0
        //
        // it does not matter if there are others query strings 
        // than the one declared in the method
        [Route("GET", "bye")]
        public object Bye(string name) {
            return $"Bye {name} !";
        }

        // test with http://localhost:2015/api/debug?a=bbbb&c=dddd
        [Route("GET", "debug")]
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


#### Path Parameters

`Route` path parameters are also supported in a similar way.
When a `{parameter}` is declared in the path, it's possible to set parameters in `Route` path and retrieve their value in the method.<br />
The library will map them according to their names.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("test/")]
    public class TestController : Controller {

        // test with http://localhost:2015/api/test/user/stratdev
        [Route("GET", "user/{login}")]
        public object User(string login) {
            return $"Hello {login}";
        }

        // test with http://localhost:2015/api/test/user/stratdev/2023
        // but
        // test with http://localhost:2015/api/test/user/stratdev/xx will
        // return a http code 500 as the "xx" cast to integer
        // will throw an exception
        [Route("GET", "user/{login}/{year}")]
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


### POST Body

You can use the `Request.Body` property to retrieve POST body data.

Frontend send POST data

```bash
curl -X POST "http://localhost:2015/api/user/save" -d 'user'
```

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("user/")]
    public class UserController : Controller {

        [Route("POST", "save")]
        public object Save() {
            return $"You sent {Request.Body}";
        }

    }

}
```


#### POST body (application/json) deserialization helper

You can use the `BodyMap()` helper method for reading POST body and deserialize to an object instance.

Frontend send POST json data

```bash
curl -X POST "http://localhost:2015/api/user/save" \
     -H "Content-Type: application/json" \
     -d '{
            id: "c037a13c-5e77-11ec-b466-e33ffd960c3a",
            name: "test",
            creation: "2021-12-21T15:06:58",
            enabled: true
        }'
```

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("user/")]
    public class UserController : Controller {

        [Route("POST", "save")]
        public object Save() {

            // instanciate User class
            var user = new User();

            try {
                // map properties from POST body to object
                Request.BodyMap(user);

                return new {
                    user
                };
            }
            // exception is thrown when type convertion failed
            catch (Exception ex) {
                return MakeInternalServerErrorResponse(ex.Message);
            }
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

Note : 

- the content-type set by client need to be `application/json` which is the default for axios.


#### POST body (application/x-www-form-urlencoded) deserialization helper

You can use the `BodyMap()` method for reading POST body and deserialize to an object instance.

Frontend send POST json data

```bash
curl -X POST "http://localhost:2015/api/user/save" \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d 'id=c037a13c-5e77-11ec-b466-e33ffd960c3a&name=test&creation=2021-12-21T15%3A06%3A58&enabled=true'
```

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("user/")]
    public class UserController : Controller {

        [Route("POST", "save")]
        public object Save() {

            // instanciate User class
            var user = new User();

            try {
                // map properties from POST body to object
                Request.BodyMap(user);

                return new {
                    user
                };
            }
            catch (Exception ex) {
                return MakeInternalServerErrorResponse(ex.Message);
            }
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

The code is exactly the same but there are some limitations due to the nature of `x-www-form-urlencoded`
specification. That's why : 
- array : only support string value and key must contain "[]" (ie: `colors[]=red,green,blue`).
- nested types (ie: `object in object`) are not supported.



#### POST body (multipart/form-data) deserialization helper

You can use the `BodyFile()` method for reading POST body containing files.

Frontend send POST json data

```bash
echo "user preferences" > prefs.json
curl -F "file=@prefs.json" "http://localhost:2015/api/user/upload" 
```

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("user/")]
    public class UserController : Controller {

        [Route("POST", "save")]
        public object Save() {

            var parser = Request.BodyFile();
            if (!parser.Files.Any(f => f.Data.Length >= 0)) {
                return "no file found in the body";
            }

            var file = parser.Files.First();
            var extension = Path.GetExtension(file.FileName).ToLower();

            // check file extension and size
            if (extension != ".json") {
                return "wrong extension";
            }
            if (file.Data.Length > 1024 * 1000) {
                return "the file size exceeds the maximum of 1Mo";
            }

            // save file
            using (var ms = new MemoryStream()) {
                try {
                    file.Data.CopyTo(ms);
                    // WARN : do not use file.FileName directly
                    //        always check and sanitize FileName to avoid injection
                    File.WriteAllBytes(file.FileName, ms.ToArray());
                }
                catch (Exception ex) {
                    return this.MakeInternalServerErrorResponse(ex.Message);
                }
            }

            return "the file has been uploaded";
        }

    }

}
```


### CORS

Internet Browser (Firefox, Chrome, IE...) blocks javascript requesting RestAPI from a different domain. That's why [CORS](https://developer.mozilla.org/fr/docs/Web/HTTP/CORS) was created, to define permission and sharing data.

To set CORS policy, use the `server.AddCORS()` method :

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");

            // set CORS policy
            server.AddCORS(
                "*",                // Access-Control-Allow-Origin
                "*",                // Access-Control-Allow-Headers
                "GET,POST,OPTIONS", // Access-Control-Allow-Methods
                "true"              // Access-Control-Allow-Credentials
            );

            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    public class SomeController : Controller {

        [Route("GET", "test")]
        public object SomePublicMethod() {
            return new {
                hello = "world"
            };
        }

    }

}
```


### Serialization

#### Default

The return of the `method` will be serialized to json using the excellent `JsonConvert.SerializeObject()` from [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/)

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    public class TestController : Controller {
        [Route("GET", "test")]
        public object Test() {
            return new {
                hello = "Hello World !",
                current = Datetime.Now,
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
    "hello": "Hello World !",
    "current": "2024-03-01T13:17:29.1249399+01:00",
    "i": 0,
    "enable": true,
    "d": {"Foo":"Bar"}
}
```

### Hooks

There are some places where SimpleW behavior can be overridden.

#### OnBeforeMethod

The `Controller` class contains an abstract method `OnBeforeMethod()` which is called before any method execution.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("test/")]
    public class TestController : Controller {

        // Router will call this methods before another one
        public override void OnBeforeMethod() {
            Console.WriteLine("OnBeforeMethod()");
        }

        [Route("GET", "index")]
        public object Index() {
            return "test index page";
        }

        [Route("POST", "create")]
        public object Create() {
            return "test create success";
        }

    }

}
```


#### Subclass

A better approach for adding some logic code to all your controllers is by extending the `Controller` class.

Example using a `BaseController` class that contains common code to all controllers.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    class BaseController : Controller {

        // example of property used in subclass
        protected Repository _repo = new();

    }

    [Route("user/")]
    class UserController : BaseController {

        [Route("GET", "index")]
        public object Index() {
            var users = _repo.GetAll<User>();
            return users;
        }

    }

    [Route("department/")]
    class DepartmentController : BaseController {

        [Route("GET", "index")]
        public object Index() {
            var departments = _repo.GetAll<Department>();
            return departments;
        }

    }

}
```

The subclass can contains route's method too. To avoid this subclass being parsed by the router, it must be excluded. Use the `AddDynamicContent()` exclude parameter.

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // exclude BaseController as a regular Controller
            server.AddDynamicContent("/api/", new Type[] { typeof(BaseController) });

            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    class BaseController : Controller {

        [Route("GET", "conf")]
        public object Conf() {
            return "conf";
        }

    }

    [Route("user/")]
    class UserController : BaseController {

    }

    [Route("department/")]
    class DepartmentController : BaseController {

    }

}
```

Note : the method `BaseController.Conf()` with its `Route` attribute is shared across all controllers. It can be access through :
- http://localhost:2015/api/user/conf
- http://localhost:2015/api/department/conf


### Properties

`Controller` class containers some useful properties.

#### Request

You can access the `Request` property inside any controller.


#### Responses

You can access the `Response` property inside any controller.


## JWT Authentication

[JSON Web Tokens](https://jwt.io/) are an open, industry standard [RFC 7519](https://tools.ietf.org/html/rfc7519) method for representing claims securely between two parties. 
SimpleW internal use the [LitJWT](https://github.com/Cysharp/LitJWT) project to forge and verify json web token.

### Get JWT

The `Controller.GetJwt()` can be used to get the raw JWT string sent by a client.

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("test/")]
    public class TestController : Controller {

        [Route("GET", "token")]
        public object Token() {
            return this.GetJwt();
        }

    }

}
```

Frontend send with JWT as a classic `Bearer Authorisation` Header

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c" \
     "http://localhost:2015/api/test/token"
```

Frontend send with JWT as `jwt` query string

```bash
curl -H "Authorization: Bearer " \
     "http://localhost:2015/api/test/token?jwt=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
```

#### Notes

There is no need to declare specific parameter in the Controller.

The `GetJwt()` will internally parse the client request looking for, by order of appearance :
1. `Session.jwt` (websocket only)
2. `jwt` querystring in the request url (api only)
3. `Authorization: bearer` in the request header (api only)

#### Why different ways for passing jwt ?

Passing jwt in the `Header` __should always__ be the preferred method.

But sometimes, header cannot be modified by client and passing jwt in the url is the only way. Example : internet browser trying to render image from `<img src= />` without javascript.

In this case, try to forge a specific JWT with role based access limited to the target ressource only and a very short period expiration (see next chapter to [forge jwt](#forge-jwt)).

#### Override GetJwt()

You can provide your own implementation of the `GetJwt()` by overriding in a [subclass](#subclass).

Example of overriding

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    class BaseController : Controller {

        // override GetJwt()
        protected override string GetJwt() {
            // 1. the jwt is extract from the "token" query string
            var route = new Route(Request);
            var qs = Route.ParseQueryString(route?.Url?.Query);
            var token = qs["token"]?.ToString();
            if (!string.IsNullOrWhiteSpace(token)) {
                return token;
            }

            // 2. the jwt is extract from "business-rule" http header
            return Request.Header("business-rule");
        }
    }

    [Route("test/")]
    class TestController : BaseController {

        [Route("GET", "token")]
        public object Token() {
            return this.GetJwt();
        }

    }

}
```

### Verify JWT

The `ValidateJwt<T>()` string extension can be used to verify a json token.

Frontend with a jwt (forge with "secret" as secret, see [jwt.io](https://jwt.io) for details)

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiaWQiOiJiODRjMDM5Yy0zY2QyLTRlN2ItODEyYy05MTQxZWQ2YzU2ZTQiLCJuYW1lIjoiSm9obiBEb2UiLCJyb2xlcyI6WyJhY2NvdW50Il0sImlhdCI6MjUxNjIzOTAyMn0.QhJ1EiMIt4uAGmYrGAC53PxoHIfX6aiWiLRbhastoB4" \
     "http://localhost:2015/api/user/account"
```

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("user/")]
    public class TestController : Controller {

        [Route("GET", "account")]
        public object Account() {
            var jwt = this.GetJwt();

            // ValidateJwt
            // Success : return an instance of T class and map jwt payload to all public properties
            // Invalid/Error : return null
            var userToken = jwt?.ValidateJwt<UserToken>("secret");

            if (userToken == null || !userToken.roles.Contains("account")) {
                return MakeUnAuthorizedResponse("private access, need account.");
            }

            return $"you have access to your account {userToken.id}";
        }

    }

    public class UserToken {
        public Guid id { get; set; }
        public string name { get; set; }
        public string[] roles { get; set; } = new string[0];
    }

}
```

The `ValidateJwt<UserToken>()` will verify token and convert payload into a `UserToken` instance.
Then, you can use `userToken` to check according to your business rules.


#### Refactor the JWT verification logic

This example shows how to integrate a global custom jwt verification in all controllers using [subclass](#subclass) and [hooks](#hooks)

Frontend send

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiaWQiOiJiODRjMDM5Yy0zY2QyLTRlN2ItODEyYy05MTQxZWQ2YzU2ZTQiLCJuYW1lIjoiSm9obiBEb2UiLCJyb2xlcyI6WyJhY2NvdW50Il0sImlhdCI6MjUxNjIzOTAyMn0.QhJ1EiMIt4uAGmYrGAC53PxoHIfX6aiWiLRbhastoB4" \
     "http://localhost:2015/api/user/account"
```

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");

            // set secret in order BaseController to verify jwt from request
            BaseController.JWT_SECRET = "secret";

            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("user/")]
    public class TestController : BaseController {

        [Route("GET", "account")]
        public object Account() {
            if (User == null || !User.roles.Contains("account")) {
                return MakeUnAuthorizedResponse("private access, need account.");
            }

            // example get user data
            // var data = database.user.get(User.id);
            return $"you have access to your account {User.id}";
        }

        [Route("GET", "infos")]
        public object Infos() {
            if (User == null || !User.roles.Contains("infos")) {
                return MakeUnAuthorizedResponse("private access, need infos.");
            }
            return "you have access to this infos";
        }

        [Route("GET", "public")]
        public object Pub() {
            return "you have access to this public";
        }

    }

    public class BaseController : Controller {

        // store jwt secret to validate
        public static string JWT_SECRET;

        // cache for user property
        private RequestUser _user;

        // flag to avoid multiple user verification in the same request
        // we can use _user as flag cause ValidateJwt can return null on error
        // so we need this extra flag to check
        private bool _user_set = false;

        // current request user
        protected RequestUser User {
            get {
                if (!_user_set) {
                    _user_set = true;
                    _user = GetJwt()?.ValidateJwt<RequestUser>(JWT_SECRET);
                }
                return _user;
            }
        }

        // if ALL your methods have to do some precheck like is user registered ?
        // just uncomment the code bellow
        //public override void OnBeforeMethod() {
        //    if (User == null) {
        //        SendResponseAsync(MakeUnAuthorizedResponse("private access, need account."));
        //    }
        //}

    }

    public class RequestUser {
        public Guid id { get; set; }
        public string name { get; set; }
        public string[] roles { get; set; } = new string[0];
    }

}
```

### Forge JWT

The `NetCoreServerExtension.CreateJwt()` method can be used to forge a json token which will be [Validate](#verify-jwt) later.


```bash
curl "http://localhost:2015/api/test/forge"
```

Backend receive

```csharp
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("test/")]
    public class TestController : Controller {

        [Route("GET", "forge")]
        public object Forge() {
            var payload = new Dictionary<string, object>() {
                { "id", Guid.NewGuid() },
                { "name", "John Doe" },
                { "roles", new string[] { "account", "infos" } }
            };
            // return the json web token string
            // with payload
            // crypt by "secret" passphrase (algo: HS256)
            // and expired in 15 minutes
            return NetCoreServerExtension.CreateJwt(payload, "secret", expiration: 15*60);
        }

    }

    public class UserToken {
        public Guid id { get; set; }
        public string name { get; set; }
        public string[] roles { get; set; } = new string[0];
    }

}
```

Note: Just browse `NetCoreServerExtension.CreateJwt()` to discover all parameters.

## Websockets

The advantage of Websockets over HTTP is the two-way communication channels : server can push data to the client without it has to request (except first time to connect socket).

More clearly : websocket avoid client polling request to server to get fresh data.

### Example Server pushing data to all clients

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

```csharp
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


### Example Server receiving data from client

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


#### Client

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
            var ws = new WebSocket('ws://localhost:2015/websocket/');
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

#### Server

Use `server.AddWebSocketContent()` to declare Controllers to a WebSocket endpoint.

The target method need to have a uniq parameter of type `WebSocketMessage` and Route Attribute must have `"WEBSOCKET"` as HTTP Verb.

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
            server.AddWebSocketContent("/websocket/");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("websocket server started at ws://localhost:2015/websocket");
            Console.ReadKey();

        }
    }

    [Route("test/")]
    public class TestController : Controller {

        // call by websocket with websocketMessage url = "/websocket/test/index"
        [Route("WEBSOCKET", "index")]
        public void Index(WebSocketMessage message) {
            Console.WriteLine("receive message");

            // response to the client
            Session.SendText("index");
        }

        // call by websocket with websocketMessage url = "/websocket/test/create"
        [Route("WEBSOCKET", "create")]
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


#### Advanced Communication between client/server

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
            var ws = new WebSocket('ws://localhost:2015/websocket/');
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
            server.AddWebSocketContent("/websocket/");

            server.Start();
            Console.WriteLine("http server started at http://localhost:2015/");
            Console.WriteLine("websocket server started at ws://localhost:2015/websocket");
            Console.ReadKey();

        }
    }

    [Route("user/")]
    public class UserController : Controller {

        // call by websocket with websocketMessage url = "/websocket/user/index"
        [Route("WEBSOCKET", "index")]
        public void Index(WebSocketMessage message) {
            Console.WriteLine($"receive message {message.body}");

            // json response to the client
            Session.SendText(JsonConvert.SerializeObject(new { hello = "world" }));
        }

        // call by websocket with websocketMessage url = "/websocket/user/create"
        [Route("WEBSOCKET", "create")]
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


## OpenTelemetry

SimpleW handle an [opentelemetry](https://github.com/open-telemetry/opentelemetry-dotnet) `Activity` and publish `Event`.

The example bellow shows how to :
- subscribe to all SimpleW telemetry events with `openTelemetryObserver()`
- log each request to console with `LogProcessor` (do not use for production).

Open browser to http://localhost:2015/api/test and console will show log.

```csharp
using System;
using System.Net;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // subscribe to all SimpleW events
            openTelemetryObserver("SimpleW");

            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }

        static TracerProvider openTelemetryObserver(string source) {
            return Sdk.CreateTracerProviderBuilder()
                              .AddSource(source)
                              .AddProcessor(new LogProcessor()) // custom log processor
                              .SetResourceBuilder(
                                  ResourceBuilder
                                      .CreateEmpty()
                                      .AddService(serviceName: "Sample", serviceVersion: "0.1")
                              ).Build();
        }

    }

    // custom log processor for opentelemetry
    class LogProcessor : BaseProcessor<Activity> {
        // write log to console
        public override void OnEnd(Activity activity) {
            // WARNING : use for local debug only not production
            Console.WriteLine($"{activity.GetTagItem("http.request.method")} \"{activity.GetTagItem("url.full")}\" {activity.GetTagItem("http.response.status_code")} {(int)activity.Duration.TotalMilliseconds}ms session-{activity.GetTagItem("session")} {activity.GetTagItem("client.address")} \"{activity.GetTagItem("user_agent.original")}\"");
        }
    }

    public class SomeController : Controller {
        [Route("GET", "test")]
        public object SomePublicMethod() {
            return new {
                hello = "world"
            };
        }
    }

}
```


For production grade, better to use well known solutions.

Uptrace is one of them can be easily integrated thanks to the [Uptrace nuget package](https://www.nuget.org/packages/Uptrace.OpenTelemetry)

See example with `openTelemetryObserver()`

```csharp
using System;
using System.Net;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Uptrace.OpenTelemetry;
using SimpleW;

namespace Sample {
    class Program {

        static void Main() {

            // subscribe to all SimpleW events
            openTelemetryObserver("SimpleW");

            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api/");
            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }

        static TracerProvider openTelemetryObserver(string source) {
            return Sdk.CreateTracerProviderBuilder()
                              .AddSource(source)
                              // see https://uptrace.dev/get/get-started.html#dsn
                              .AddUptrace("uptrace_connection_string_api_key")
                              .SetResourceBuilder(
                                  ResourceBuilder
                                      .CreateEmpty()
                                      .AddService(serviceName: "Sample", serviceVersion: "0.1")
                              ).Build();
        }

    }

    public class SomeController : Controller {
        [Route("GET", "test")]
        public object SomePublicMethod() {
            return new {
                hello = "world"
            };
        }
    }

}
```

## Why i wrote this library

To my opinion, modern web application architecture should be based on a REST API which acts as a contract between 2 parts :
- backend (only one) : developer feels free to use/change the technology he wants (C#, Go, Rust, PHP...) but must provide and follow the REST API.
- frontend (one or many) : developer feels free to use/change the technology he wants (SPA/Vue, SPA/React, Mobile/Android...) but must consume and follow the REST API.

### So, my needs

#### Frontend

I prefer [SPA](https://en.wikipedia.org/wiki/Single-page_application) using [Vite](https://vitejs.dev/), [Vue](https://vuejs.org) and [Vuetify](https://vuetifyjs.com).

#### Backend

- written in C#, the language i 😍.
- must be easy to integrate, lightweight with a minimal footprint.
- must support Routing, Websocket, CORS.
- don't need to have template engine as i write frontend in a separated project.
- must serve static files (static files are the result of my `npm run build` vite project)


### The existings projects
- [ASP.NET Core](https://learn.microsoft.com/fr-fr/aspnet/core/?view=aspnetcore-8.0) :
    - too many features i don't need, i don't want _(Razor, Blazor...)_.
    - overcomplicated to customize some behaviour
    - too heavy, sometimes i have a very small API.
- [IIS](https://iis.net/) an old _« usine à gaz »_ on Windows, Kestrel and SignalR the same on Linux.
- [EmbedIO](https://github.com/unosquare/embedio) : long time v2 user, i dislike the rewrite of the v3. Moreover, it uses the old Microsoft `HttpListener` and the `websocket-sharp` alternative was not perfect.
- [GenHttp](https://genhttp.org) : feels promising but i was in the process of writting my own.
- __[NetCoreServer](https://github.com/chronoxor/NetCoreServer)__ : WHOA 😮 ! Fast, simple, extremly well design, extendable BUT no RESTAPI... Wait, what if i use the whole `OnReceivedRequest()` event to do exactly what i want 🤔

### This project

SimpleW is the result of adding basic RESTAPI features to the `OnReceivedRequest()` of [NetCoreServer](https://github.com/chronoxor/NetCoreServer).

After 2 years grade production, SimpleW serves many APIs without any issue, gains some cool features but still always lightweight and easy to integrate.

Feel free to report issue.

# License
This library is under the MIT License.