using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");
            await server.RunAsync();
            Console.WriteLine("server started at http://localhost:{server.Port}/");
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