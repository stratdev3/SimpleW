using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);

            // exclude BaseController as a regular Controller
            server.MapControllers<Controller>("/api", new Type[] { typeof(BaseController) });

            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
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