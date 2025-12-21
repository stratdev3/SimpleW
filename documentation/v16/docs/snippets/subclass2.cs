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