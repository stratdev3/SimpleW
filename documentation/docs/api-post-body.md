# POST Body

## Basic Example

You can use the `Request.Body` property to retrieve POST body data.

Frontend send POST data

```bash
curl -X POST "http://localhost:2015/api/user/save" -d 'user'
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
    public class UserController : Controller {

        [Route("POST", "/save")]
        public object Save() {
            return $"You sent {Request.Body}";
        }

    }

}
```


## POST body (application/json) deserialization helper

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
    public class UserController : Controller {

        [Route("POST", "/save")]
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


## POST body (application/x-www-form-urlencoded) deserialization helper

You can use the `BodyMap()` method for reading POST body and deserialize to an object instance.

Frontend send POST json data

```bash
curl -X POST "http://localhost:2015/api/user/save" \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d 'id=c037a13c-5e77-11ec-b466-e33ffd960c3a&name=test&creation=2021-12-21T15%3A06%3A58&enabled=true'
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
    public class UserController : Controller {

        [Route("POST", "/save")]
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



## POST body (multipart/form-data) deserialization helper

You can use the `BodyFile()` method for reading POST body containing files.

Frontend send POST json data

```bash
echo "user preferences" > prefs.json
curl -F "file=@prefs.json" "http://localhost:2015/api/user/upload" 
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
    public class UserController : Controller {

        [Route("POST", "/save")]
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
