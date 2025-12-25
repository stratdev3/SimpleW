# Request

The [`Request`](../reference/httprequest.md) property of [`HttpSession`](../reference/httpsession.md#request) class contains all the information (Url, Headers, Method, Protocol, Body...) about the request sent by the client.


## BodyString

You can use the [`Request.BodyString`](../reference/httprequest#bodystring) property to retrieve the data from any `POST` request.

Frontend send POST data

```bash
curl -X POST "http://localhost:2015/api/user/save" -d 'data in the body'
```

Backend receive

```csharp:line-numbers {22}
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    [Route("/user")]
    public class UserController : Controller {

        [Route("POST", "/save")]
        public object Save() {
            return $"You sent {Request.BodyString}";
        }

    }

}
```

The response will contains :

```
You sent data in the body
```


## Body (application/json) deserialization helper

You can use the [`BodyMap()`](../reference/httprequest#bodymap) helper method for reading [`Request.BodyString`](../reference/httprequest#bodystring) and deserialize to an object instance.

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
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
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
                return Response.MakeInternalServerErrorResponse(ex.Message);
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

::: tip NOTE
The content-type set by client need to be `application/json`.
:::


## Body (application/x-www-form-urlencoded) deserialization helper

You can use the [`BodyMap()`](../reference/httprequest#bodymap) method for reading [`Request.BodyString`](../reference/httprequest#bodystring) and deserialize to an object instance.

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
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
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
                return Response.MakeInternalServerErrorResponse(ex.Message);
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



## Body (multipart/form-data) deserialization helper

You can use the [`BodyMultipart()`](../reference/httprequest#bodymultipart) method for reading [`Request.Body`](../reference/httprequest#body) containing files.

Frontend send a file POST

```bash
echo "hello server !" > message.txt
curl -F "file=@message.txt" "http://localhost:2015/api/user/upload"
```

Backend receive

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    [Route("/user")]
    public class UserController : Controller {

        [Route("POST", "/upload")]
        public object Upload() {

            var parser = Request.BodyMultipart();
            if (parser == null || parser.Files.Any(f => f.Content.Length >= 0)) {
                return "no file found in the body";
            }

            var file = parser.Files.First();
            var extension = Path.GetExtension(file.FileName).ToLower();

            // check file extension and size
            if (extension != ".txt") {
                return "wrong extension";
            }
            if (file.Data.Length > 1_000 * 1024) {
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
                    return Response.MakeInternalServerErrorResponse(ex.Message);
                }
            }

            return "the file has been uploaded and saved to server";
        }

    }

}
```

There is also a [`BodyMultipartStream()`](../reference/httprequest#bodymultipartstream) method that use `Stream` for better performances

```csharp:line-numbers
using System;
using System.Net;
using SimpleW;

namespace Sample {

    class Program {
        static async Task Main() {
            var server = new SimpleWServer(IPAddress.Any, 2015);
            server.MapControllers<Controller>("/api");
            Console.WriteLine("server started at http://localhost:{server.Port}/");
            await server.RunAsync();
        }
    }

    [Route("/user")]
    public class UserController : Controller {

        [Route("POST", "/upload")]
        public async ValueTask<HttpResponse> Upload() {
            Directory.CreateDirectory(UploadDir);

            string? title = null;

            var saved = new List<object>();

            bool ok = Request.BodyMultipartStream(
                onField: (k, v) => {
                    if (string.Equals(k, "title", StringComparison.OrdinalIgnoreCase)) {
                        title = v;
                    }
                },
                onFile: (info, content) => {

                    string originalName = info.FileName ?? "";
                    string safeName = Path.GetFileName(originalName);
                    if (string.IsNullOrWhiteSpace(safeName)) {
                        safeName = "upload.bin";
                    }

                    // avoid name collisions with a suffix
                    string finalName = $"{Path.GetFileNameWithoutExtension(safeName)}_{Guid.NewGuid():N}{Path.GetExtension(safeName)}";
                    string fullPath = Path.Combine(UploadDir, finalName);

                    using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024);
                    content.CopyTo(fs);

                    saved.Add(new {
                        field = info.FieldName,
                        originalName = originalName,
                        savedAs = finalName,
                        size = info.Length,
                        contentType = info.ContentType
                    });
                },
                maxParts: 200,
                maxFileBytes: Session.Request.MaxRequestBodySize
            );

            if (!ok) {
                return Session.Response
                              .Status(400)
                              .Json(new { ok = false, error = "Invalid multipart/form-data" });
            }

            // json response
            return Session.Response
                          .Status(200)
                          .Json(new {
                              ok = true,
                              title,
                              uploadDir = UploadDir,
                              files = saved
                          });
        }

    }

}
```
