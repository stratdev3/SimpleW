using System.Text;
using SimpleW;


namespace example;

internal sealed class RequestScenario : IScenario {

    public string Name => "request";

    public string Description => "HTTP methods, request-body echo, headers and cookies.";

    public string Usage => string.Empty;

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed();

        SimpleWServer server = ExampleServer.Create(arguments);
        server.MapGet("/", () => "OK");
        server.Map("HEAD", "/", () => "OK");
        server.Map("OPTIONS", "/", (HttpSession session) => {
            return session.Response.AddHeader("Allow", "GET, HEAD, POST, OPTIONS").Text("OK");
        });
        server.MapPost("/", (HttpSession session) => session.Response.Text(session.Request.BodyString));
        server.MapGet("/echo", (HttpSession session) => EchoHeaders(session));
        server.MapPost("/echo", (HttpSession session) => EchoHeaders(session));
        server.MapGet("/cookie", (HttpSession session) => EchoCookies(session));
        server.MapPost("/cookie", (HttpSession session) => EchoCookies(session));

        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

    private static HttpResponse EchoHeaders(HttpSession session) {
        StringBuilder body = new();
        foreach (KeyValuePair<string, string> header in session.Request.Headers.EnumerateAll()) {
            body.AppendLine($"{header.Key}: {header.Value}");
        }
        return session.Response.Text(body.ToString());
    }

    private static HttpResponse EchoCookies(HttpSession session) {
        StringBuilder body = new();
        foreach (KeyValuePair<string, string> cookie in session.Request.Headers.EnumerateCookies()) {
            body.AppendLine($"{cookie.Key}={cookie.Value}");
        }
        return session.Response.Text(body.ToString());
    }

}
