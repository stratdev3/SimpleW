using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SimpleW;
using SimpleW.Modules;


namespace example.rewrite {

    /// <summary>
    /// Example Program
    /// perf http : bombardier -c 200 -d 10s http://127.0.0.1:8080/api/test/hello
    /// perf https : bombardier -c 200 -d 10s -k https://127.0.0.1:8080/api/test/hello
    /// </summary>
    internal class Program {

        /// <summary>
        /// EntryPoint
        /// </summary>
        /// <param name="args"></param>
        static async Task Main(string[] args) {
            await Rewrite();
            //await Raw();
        }

        static async Task Rewrite() {

            SimpleWServer server = new(IPAddress.Any, 8080);

#pragma warning disable CS0162 // Code inaccessible détecté
            if (false) {
                // echo subjectAltName=DNS:localhost,IP:127.0.0.1 > san.cnf && openssl genrsa -out privkey.pem 2048 && openssl req -new -key privkey.pem -out cert.csr -subj "/C=FR/O=SimpleW/OU=Dev/CN=localhost/emailAddress=admin@localhost" && openssl x509 -req -days 1095 -in cert.csr -signkey privkey.pem -out cert.pem -extfile san.cnf && openssl pkcs12 -export -out simplew.pfx -inkey privkey.pem -in cert.pem -password pass:secret && del san.cnf
                string SslCertificateBase64 = "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";
                string certificateFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "simplew.pfx");
                Directory.CreateDirectory(Path.Combine(certificateFilePath, ".."));
                File.WriteAllBytes(certificateFilePath, Convert.FromBase64String(SslCertificateBase64));

                // create a context with certificate, support for password protection
#if NET9_0_OR_GREATER
                X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(certificateFilePath, "secret");
#else
                X509Certificate2 cert = new(certificateFilePath, "password");
#endif
                SslContext sslContext = new(
                    SslProtocols.Tls12 | SslProtocols.Tls13,
                    cert,
                    clientCertificateRequired: false,
                    checkCertificateRevocation: false
                );
                server.UseHttps(sslContext);
            }
#pragma warning restore CS0162 // Code inaccessible détecté

            //server.MapGet("/", () => {
            //    return server.Router.Routes;
            //});
            // use middleware as firewall/authenticate
            //server.UseMiddleware(static (session, next) => {
            //    // check if the user is authorized ?
            //    if (session.Request.Path.StartsWith("/api/secret", StringComparison.Ordinal)) {
            //        if (!session.Request.Headers.TryGetValue("X-Api-Key", out var key) || key != "secret") {
            //            // stop the pipeline here by sending a 401
            //            return session.Response.Unauthorized("You're authorized in this area").SendAsync();
            //        }
            //    }
            //    // continue the pipeline
            //    return next();
            //});


            server.MapGet("/api/test/hello", (string? name = null) => {
                return new { message = $"{name}, Hello World !" };
            });
            //server.MapGet("/api/test/hello", static (HttpSession session, string? name = null) => {
            //    return session.Response.Json(new { message = $"{name} Hello World !" }).SendAsync();
            //});
            //server.MapGet("/api/user/*", static async ValueTask<object> (HttpSession session, int? id = 999999) => {
            //    if (id == 999999) {
            //        await Task.Delay(2_000);
            //        return session.Response.Status(404);
            //    }
            //    return new { message = "Hello World !", id };
            //});
            //server.UseModule(new StaticFilesModule(
            //    @"C:\www\spa\refresh\", "/"
            //    , timeout: TimeSpan.FromDays(1)
            //) {
            //    AutoIndex = true
            //});

            //server.UseStaticFilesModule(options => {
            //    options.Path = @"C:\www\spa\refresh\";
            //    options.Prefix = "/";
            //    options.CacheTimeout = TimeSpan.FromDays(1);
            //});

            //server.UseModule(
            //    new WebsocketModule(
            //        path: "/websocket",
            //        onClient: async (ws, session) => {

            //            if (ws.SubProtocol != "json") {
            //                await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "json required", CancellationToken.None);
            //                return;
            //            }

            //            byte[] buffer = new byte[4096];

            //            while (ws.State == WebSocketState.Open) {
            //                WebSocketReceiveResult result = await ws.ReceiveAsync(buffer, CancellationToken.None);

            //                if (result.MessageType == WebSocketMessageType.Close) {
            //                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            //                    break;
            //                }
            //                if (result.MessageType != WebSocketMessageType.Text) {
            //                    await ws.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "Text(JSON) only", CancellationToken.None);
            //                    return;
            //                }

            //                // validate JSON
            //                try {
            //                    // reassembly minimal (si tu veux être carré: concat jusqu'à EndOfMessage)
            //                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
            //                    using var _ = JsonDocument.Parse(text);
            //                }
            //                catch {
            //                    await ws.SendAsync(
            //                        Encoding.UTF8.GetBytes("""{"type":"error","message":"invalid json"}"""),
            //                        WebSocketMessageType.Text,
            //                        true,
            //                        CancellationToken.None
            //                    );
            //                    continue;
            //                }

            //                // echo
            //                await ws.SendAsync(
            //                    buffer.AsMemory(0, result.Count),
            //                    result.MessageType,
            //                    result.EndOfMessage,
            //                    CancellationToken.None
            //                );
            //            }

            //        }
            //    )
            //);

            //server.MapControllers<Controller>("/api");
            //server.MapController<Post_DynamicContent_HelloWorld_Controller>("/api");

            server.OptionReuseAddress = true;
            server.OptionNoDelay = true;
            server.OptionKeepAlive = true;
            //server.OptionSessionTimeout = TimeSpan.MinValue;
            //server.OptionRunAcceptSocketPerCore = true;
            //server.OptionReceiveStrategy = SimpleW.ReceivedStrategy.ReceiveLoopBuffer;
            //server.OptionMaxRequestBodySize = 100 * 1024 * 1024;

            // start non blocking background server
            CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"server started at http://localhost:{server.Port}/api/test/hello");
            await server.RunAsync(cts.Token);
            Console.WriteLine("server stopped");
        }

        static async Task Raw() {
            RawServer.RawServer server = new(IPAddress.Any, 8080);

            server.OptionReuseAddress = true;
            server.OptionNoDelay = true;
            server.OptionKeepAlive = true;

            // start non blocking background server
            CancellationTokenSource cts = new();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"server started at http://localhost:{server.Port}/api/test/hello");
            await server.StartAsync(cts.Token);
            Console.WriteLine("server stopped");
        }

    }

    [Route("/test")]
    public class TestController : Controller {

        [Route("GET", "/hello")]
        public object Hello(string? name = null) {

            // the return will be serialized to json
            //return new {
            //    message = $"{name}, Hello World !"
            //};
            string message = "Hello World";
            for (var i = 0; i < 10; i++) {
                message += message;
            }
            
            return Session.Response.Json(new { message });
        }

        [Route("POST", "/api/user/update", isAbsolutePath: true)]
        public object UserUpdate() {

            User chris = new();
            Request.BodyMap(chris);
            
            return Session.Response.Json(new { message = "ok", chris });
        }


    }


    public class User {
        public string nom { get; set; }
        public string prenom { get; set; }
        public int age { get; set; }
    }


    public sealed class UploadController : Controller {
        private static readonly string UploadDir = @"C:\www\spa\tmp\";

        [Route("POST", "/upload", isAbsolutePath: true)]
        public async ValueTask<HttpResponse> Upload() {
            Directory.CreateDirectory(UploadDir);

            string? title = null;

            // On collecte des infos à renvoyer
            var saved = new List<object>();

            bool ok = Request.BodyMultipartStream(
                onField: (k, v) => {
                    if (string.Equals(k, "title", StringComparison.OrdinalIgnoreCase))
                        title = v;
                },
                onFile: (info, content) => {
                    // filename vient du client => sanitize + fallback
                    string originalName = info.FileName ?? "";
                    string safeName = Path.GetFileName(originalName);
                    if (string.IsNullOrWhiteSpace(safeName))
                        safeName = "upload.bin";

                    // évite les collisions: ajoute un suffixe
                    string finalName = $"{Path.GetFileNameWithoutExtension(safeName)}_{Guid.NewGuid():N}{Path.GetExtension(safeName)}";
                    string fullPath = Path.Combine(UploadDir, finalName);

                    using var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024);
                    content.CopyTo(fs); // extension ReadOnlySequence<byte> -> Stream (qu’on a ajoutée)

                    saved.Add(new {
                        field = info.FieldName,
                        originalName = originalName,
                        savedAs = finalName,
                        size = info.Length,
                        contentType = info.ContentType
                    });
                },
                maxParts: 200,
                maxFileBytes: Session.Server.OptionMaxRequestBodySize
            );

            if (!ok) {
                return Session.Response
                    .Status(400)
                    .Json(new { ok = false, error = "Invalid multipart/form-data" });
            }

            // réponse JSON
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


    [Route("/test")]
    public class Post_DynamicContent_HelloWorld_Controller : Controller {

        [Route("POST", "/hello")]
        public object HelloWorld() {
            var user = new User();
            Request.BodyMap(user);

            return new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" };
        }

        [Route("POST", "/raw")]
        public object Raw() {
            return Session.Response.Text(Session.Request.BodyString);
        }

        [Route("POST", "/file")]
        public object File() {
            var parser = Request.BodyMultipart();
            if (parser == null || parser.Files.Any(f => f.Content.Length >= 0)) {
                return "no file found in the body";
            }

            var file = parser.Files.First();
            var extension = Path.GetExtension(file.FileName).ToLower();

            var content = "";
            try {
                content = Encoding.UTF8.GetString(file.Content.ToArray());
            }
            catch (Exception ex) {
                return Session.Response.Status(500).Json(ex.Message);
            }

            var name = parser.Fields.Where(p => p.Key == nameof(Post_DynamicContent_HelloWorld_Controller.User.Name)).FirstOrDefault().Value;

            return new { message = $"{name}, {content}" };
        }

        public class User {
            public Guid Id { get; set; }
            public bool Enabled { get; set; }
            public string? Name { get; set; }
            public int Counter { get; set; }
            public DateTime CreatedAt { get; set; }
        }

    }


}
