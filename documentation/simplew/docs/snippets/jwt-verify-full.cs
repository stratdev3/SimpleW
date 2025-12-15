using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static void Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.AddDynamicContent("/api");

            // set secret in order BaseController to verify jwt from request
            BaseController.JWT_SECRET = "secret";

            server.Start();
            Console.WriteLine("server started at http://localhost:2015/");
            Console.ReadKey();
        }
    }

    [Route("/user")]
    public class TestController : BaseController {

        [Route("GET", "/account")]
        public object Account() {
            if (User == null || !User.roles.Contains("account")) {
                return MakeUnAuthorizedResponse("private access, need account.");
            }

            // example get user data
            // var data = database.user.get(User.id);
            return $"you have access to your account {User.id}";
        }

        [Route("GET", "/infos")]
        public object Infos() {
            if (User == null || !User.roles.Contains("infos")) {
                return MakeUnAuthorizedResponse("private access, need infos.");
            }
            return "you have access to this infos";
        }

        [Route("GET", "/public")]
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