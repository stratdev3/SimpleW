# Request

The [`Request`](../reference/httprequest.md) object represents everything the client sends to the server.

It is available from both execution contexts :
- [`HttpSession.Request`](../reference/httpsession.md#request)
- [`Controller.Request`](../reference/controller.md#request)

Request exposes all HTTP-level data :
- HTTP method (GET, POST, …)
- Path and raw target
- Query string
- Route parameters
- Headers
- Body (raw bytes, string, parsed forms, files)

Conceptually :

> Request is a read-only snapshot of the incoming HTTP request, valid only during handler execution.

## Request Lifecycle and Safety

The request body is **only valid while the handler is running**.

- Internal buffers may be reused after execution
- Do not store references to `Request.Body`
- Copy data if you need to keep it beyond the handler

This design avoids allocations and keeps SimpleW fast and predictable.


## Query String and Route Values

Request also exposes structured request metadata :
- [`Request.Query`](../reference/httprequest.md#query) – parsed query string
- [`Request.RouteValues`](../reference/httprequest.md#routevalues) – extracted route parameters

Example :

```bash
GET /users/42?active=true
```

```csharp
[Route("GET", "/users/:id")]
public object Get(int id, bool active = false) {
    return new { id, active };
}
```


## Reading the Request Body

### BodyString

[`Request.BodyString`](../reference/httprequest.md#bodystring) returns the request body as a UTF-8 string.<br />
It is suitable for: POST, PUT, PATCH

### Example

Client sends data :

```bash
curl -X POST "http://localhost:2015/api/user/save" -d "data in the body"
```

Server :

::: code-group

```csharp [program.cs]
class Program {
    static async Task Main() {
        var server = new SimpleWServer(IPAddress.Any, 2015);
        server.MapControllers<Controller>("/api");
        Console.WriteLine("server started at http://localhost:{server.Port}/");
        await server.RunAsync();
    }
}
```

```csharp [UserController.cs]
[Route("/user")]
public class UserController : Controller {

    [Route("POST", "/save")]
    public object Save() {
        return $"You sent {Request.BodyString}";
    }

}
```

:::

Response :

```
You sent data in the body
```

### Mental Model

> `BodyString` is a convenience accessor, not a streaming API.

Use it for small payloads and simple cases.


## JSON Body Deserialization (application/json)

For JSON payloads, SimpleW provides the [`BodyMap()`](../reference/httprequest.md#bodymap) helper :
- Reads `Request.BodyString`
- Deserializes JSON
- Populates an existing object instance

### Example

Client :

```bash
curl -X POST "http://localhost:2015/api/user/save" \
     -H "Content-Type: application/json" \
     -d '{
          "id": "c037a13c-5e77-11ec-b466-e33ffd960c3a",
          "name": "test",
          "creation": "2021-12-21T15:06:58",
          "enabled": true
     }'
```

Server :

::: code-group

```csharp [program.cs]
class Program {
    static async Task Main() {
        var server = new SimpleWServer(IPAddress.Any, 2015);
        server.MapControllers<Controller>("/api");
        Console.WriteLine("server started at http://localhost:{server.Port}/");
        await server.RunAsync();
    }
}
```

```csharp [UserController.cs]
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
```

:::

::: tip NOTE
The client must send `Content-Type: application/json`.
:::

### Mental Model

> SimpleW never deserializes implicitly — you explicitly decide when to parse the body.



## Form Body (application/x-www-form-urlencoded)

[`BodyMap()`](../reference/httprequest.md#bodymap) also supports classic HTML form payloads.

Client :

```bash
curl -X POST "http://localhost:2015/api/user/save" \
     -H "Content-Type: application/x-www-form-urlencoded" \
     -d "id=...&name=test&creation=2021-12-21T15%3A06%3A58&enabled=true"
```

Server code remains exactly the same :

```csharp
Request.BodyMap(user);
```

### Limitations

Due to the nature of `x-www-form-urlencoded`:
- Arrays must use `[]` syntax and support strings only (`colors[]=red&colors[]=blue`)
- Nested objects are not supported

### Mental Model

> Form data is flat key/value data, not structured JSON.


## Multipart Form Data (multipart/form-data)

For file uploads, SimpleW provides two APIs :
- [`BodyMultipart()`](../reference/httprequest.md#bodymultipart) – in-memory parsing
- [`BodyMultipartStream()`](../reference/httprequest.md#bodymultipartstream) – streaming, high-performance parsing


### BodyMultipart()

Suitable for small uploads.

Client :

```bash
echo "hello server !" > message.txt
curl -F "file=@message.txt" "http://localhost:2015/api/user/upload"
```

Server :

::: code-group

```csharp [program.cs]
class Program {
    static async Task Main() {
        var server = new SimpleWServer(IPAddress.Any, 2015);
        server.MapControllers<Controller>("/api");
        Console.WriteLine("server started at http://localhost:{server.Port}/");
        await server.RunAsync();
    }
}
```

```csharp [UserController.cs]
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
```

:::

::: tip NOTE
Always sanitize `FileName` before writing to disk.
:::


### BodyMultipartStream()

Recommended for production and large uploads.

Key characteristics :
- Constant memory usage
- Streaming I/O
- Explicit limits (`maxParts`, `maxFileBytes`)

::: code-group

```csharp [program.cs]
class Program {
    static async Task Main() {
        var server = new SimpleWServer(IPAddress.Any, 2015);
        server.MapControllers<Controller>("/api");
        Console.WriteLine("server started at http://localhost:{server.Port}/");
        await server.RunAsync();
    }
}
```

```csharp [UserController.cs]
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
```

:::

### Mental Model

> Multipart streaming gives you full control without loading files into memory.
