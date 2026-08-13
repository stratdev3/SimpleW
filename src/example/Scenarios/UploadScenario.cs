using SimpleW;


namespace example;

internal sealed class UploadScenario : IScenario {

    private const string UploadPage = """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>SimpleW upload</title></head>
        <body>
          <h1>Multipart upload</h1>
          <form action="/upload" method="post" enctype="multipart/form-data">
            <label>Name <input name="name" value="SimpleW"></label><br>
            <label>File <input name="file" type="file" required></label><br>
            <button type="submit">Upload</button>
          </form>
        </body>
        </html>
        """;

    public string Name => "upload";

    public string Description => "Multipart upload form returning field and file metadata without saving.";

    public string Usage => string.Empty;

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        SimpleWServer server = ExampleServer.Create(arguments);
        server.MapGet("/", (HttpSession session) => session.Response.Html(UploadPage));
        server.MapPost("/upload", object (HttpSession session) => {
            MultipartFormData? form = session.Request.BodyMultipart();
            if (form == null) {
                return session.Response.Status(400).Json(new { error = "Expected multipart/form-data." });
            }

            return new {
                fields = form.Fields,
                files = form.Files.Select(file => new {
                    field = file.FieldName,
                    name = file.FileName,
                    contentType = file.ContentType,
                    size = file.Content.Length
                })
            };
        });

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
