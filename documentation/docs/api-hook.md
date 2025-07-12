# Hook


There are some places where SimpleW behavior can be overridden.

## OnBeforeMethod

The `Controller` class contains an abstract method `OnBeforeMethod()` which is called before any method execution.

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

        // Router will call this methods before another one
        public override void OnBeforeMethod() {
            Console.WriteLine("OnBeforeMethod()");
        }

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


## Subclass

A better approach for adding some logic code to all your controllers is by extending the `Controller` class.

Example using a `BaseController` class that contains common code to all controllers.

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

    class BaseController : Controller {

        // example of property used in subclass
        protected Repository _repo = new();

    }

    [Route("/user")]
    class UserController : BaseController {

        [Route("GET", "/index")]
        public object Index() {
            var users = _repo.GetAll<User>();
            return users;
        }

    }

    [Route("/department")]
    class DepartmentController : BaseController {

        [Route("GET", "/index")]
        public object Index() {
            var departments = _repo.GetAll<Department>();
            return departments;
        }

    }

}
```

The subclass can contains route's method too. To avoid this subclass being parsed by the router, it must be excluded. Use the `AddDynamicContent()` exclude parameter.

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // exclude BaseController as a regular Controller
            server.AddDynamicContent("/api", new Type[] { typeof(BaseController) });

            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    class BaseController : Controller {

        [Route("GET", "/conf")]
        public object Conf() {
            return "conf";
        }

    }

    [Route("/user")]
    class UserController : BaseController {

    }

    [Route("/department")]
    class DepartmentController : BaseController {

    }

}
```

Note : the method `BaseController.Conf()` with its `Route` attribute is shared across all controllers. It can be access through :
- http://localhost:2015/api/user/conf
- http://localhost:2015/api/department/conf
