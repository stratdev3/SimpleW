# How To

This page collects practical recipes built from multiple SimpleW features.

It intentionally focuses on use cases that are not already covered step by step in the rest of the guide.


## Force download for generated content

When you generate content dynamically and want the browser to download it instead of displaying it inline,
return a normal `HttpResponse` and add `Attachment(...)`.

```csharp
using System.Net;
using System.Text;
using SimpleW;

var server = new SimpleWServer(IPAddress.Any, 8080);

server.MapGet("/reports/users.csv", (HttpSession session) => {
    string csv = "id,name,email\n"
                 + "1,Alice,alice@example.com\n"
                 + "2,Bob,bob@example.com\n";

    byte[] bytes = Encoding.UTF8.GetBytes(csv);

    return session.Response
                  .Body(bytes)
                  .Attachment("users.csv")
                  .ContentType("text/csv; charset=utf-8");
});
```

This pattern also works for:

- generated invoices
- exports
- temporary diagnostics bundles
- one-off backups
