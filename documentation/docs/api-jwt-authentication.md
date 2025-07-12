# JWT Authentication


[JSON Web Tokens](https://jwt.io/) are an open, industry standard [RFC 7519](https://tools.ietf.org/html/rfc7519) method for representing claims securely between two parties. 
SimpleW internal use the [LitJWT](https://github.com/Cysharp/LitJWT) project to forge and verify json web token.

## Get Token

The `Controller.GetJwt()` can be used to get the raw JWT string sent by a client.

Backend receive

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

        [Route("GET", "/token")]
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

### Notes

There is no need to declare specific parameter in the Controller.

The `GetJwt()` will internally parse the client request looking for, by order of appearance :
1. `Session.jwt` (websocket only)
2. `jwt` querystring in the request url (api only)
3. `Authorization: bearer` in the request header (api only)

### Why different ways for passing jwt ?

Passing jwt in the `Header` __should always__ be the preferred method.

But sometimes, header cannot be modified by client and passing jwt in the url is the only way. Example : internet browser trying to render image from `<img src= />` without javascript.

In this case, try to forge a specific JWT with role based access limited to the target ressource only and a very short period expiration (see next chapter to [forge jwt](#forge-jwt)).

### Override GetJwt()

You can provide your own implementation of the `GetJwt()` by overriding in a [subclass](#subclass).

Example of overriding

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

    [Route("/test")]
    class TestController : BaseController {

        [Route("GET", "/token")]
        public object Token() {
            return this.GetJwt();
        }

    }

}
```

## Verify Token

The `ValidateJwt<T>()` string extension can be used to verify a json token.

Frontend with a jwt (forge with "secret" as secret, see [jwt.io](https://jwt.io) for details)

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiaWQiOiJiODRjMDM5Yy0zY2QyLTRlN2ItODEyYy05MTQxZWQ2YzU2ZTQiLCJuYW1lIjoiSm9obiBEb2UiLCJyb2xlcyI6WyJhY2NvdW50Il0sImlhdCI6MjUxNjIzOTAyMn0.QhJ1EiMIt4uAGmYrGAC53PxoHIfX6aiWiLRbhastoB4" \
     "http://localhost:2015/api/user/account"
```

Backend receive

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

    [Route("/user")]
    public class TestController : Controller {

        [Route("GET", "/account")]
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


### Refactor the JWT verification logic

This example shows how to integrate a global custom jwt verification in all controllers using [subclass](#subclass) and [hooks](#hooks)

Frontend send

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiaWQiOiJiODRjMDM5Yy0zY2QyLTRlN2ItODEyYy05MTQxZWQ2YzU2ZTQiLCJuYW1lIjoiSm9obiBEb2UiLCJyb2xlcyI6WyJhY2NvdW50Il0sImlhdCI6MjUxNjIzOTAyMn0.QhJ1EiMIt4uAGmYrGAC53PxoHIfX6aiWiLRbhastoB4" \
     "http://localhost:2015/api/user/account"
```

Backend receive

```csharp:line-numbers
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
```

## Forge Token

The `NetCoreServerExtension.CreateJwt()` method can be used to forge a json token which will be [Validate](#verify-jwt) later.


```bash
curl "http://localhost:2015/api/test/forge"
```

Backend receive

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

        [Route("GET", "/forge")]
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
