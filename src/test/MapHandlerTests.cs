using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Map Handler
    /// </summary>
    public class MapHandlerTests {

        #region MapGet

        [Fact]
        public async Task MapGet_NoParameter_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterName_NameHelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string? name = null) => {
                return new { message = $"{name}, Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterName_Null1HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string? name = null) => {
                return new { message = $"{name}, Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = ", Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterName_Null2HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string? name = null) => {
                return new { message = $"{name}, Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = ", Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterSessionRequest_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                Check.That(session).IsNotNull();
                Check.That(session.Request).IsNotNull();
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterSessionRequestName_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session, string? name = null) => {
                Check.That(session).IsNotNull();
                Check.That(session.Request).IsNotNull();
                return new { message = $"{name}, Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterNameRequestSession_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string name, HttpSession session) => {
                Check.That(session).IsNotNull();
                Check.That(session.Request).IsNotNull();
                return new { message = $"{name}, Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion MapGet

        #region MapPost

        [Fact]
        public async Task BodyMap_Raw_MapPost_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (HttpSession session) => {
                return Raw(session);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = "Chris";
            var payload = new StringContent(user);
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(user);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        /*
        // TODO : there is a exception thrown by system.text.json deserializer cause incorrect type ("True" or "0")
        [Fact]
        public async Task BodyMap_formurlencoded_MapPost_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (HttpSession session) => {
                return HelloWorld(session);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = new Post_MapPost_HelloWorld_Controller.User() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            var payload = new FormUrlEncodedContent(new[]{
                new KeyValuePair<string, string>("Id", user.Id.ToString()),
                new KeyValuePair<string, string>("Name", user.Name),
                new KeyValuePair<string, string>("Counter", user.Counter.ToString()),
                new KeyValuePair<string, string>("CreatedAt", user.CreatedAt.ToString("o")),
                new KeyValuePair<string, string>("Enabled", user.Enabled.ToString())
            });
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }
        */

        [Fact]
        public async Task BodyMap_Json_MapPost_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (HttpSession session) => {
                return HelloWorld(session);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = new Post_MapPost_HelloWorld_Controller.User() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            var json = JsonSerializer.Serialize(user);
            var payload = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task BodyMap_File_MapPost_HelloWorld() {

            string testFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "BodyMap_File_MapPost_HelloWorld", "file.txt");
            string testFileContent = "Hello World !";
            Directory.CreateDirectory(Path.Combine(testFilePath, ".."));
            File.WriteAllText(testFilePath, testFileContent);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (HttpSession session) => {
                return FileResponse(session);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            using var payload = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(testFilePath);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            payload.Add(fileContent, "file", Path.GetFileName(testFilePath));
            var user = new Post_MapPost_HelloWorld_Controller.User() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            payload.Add(new StringContent(user.Name), nameof(Post_MapPost_HelloWorld_Controller.User.Name));
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = $"{user.Name}, {testFileContent}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        public object HelloWorld(HttpSession session) {
            var user = new Post_MapPost_HelloWorld_Controller.User();
            session.Request.BodyMap(user);

            return new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" };
        }

        public object Raw(HttpSession session) {
            return session.Response.Text(session.Request.BodyString);
        }

        public object FileResponse(HttpSession session) {

            var parser = session.Request.BodyMultipart();
            if (parser == null || !parser.Files.Any(f => f.Content.Length >= 0)) {
                return "no file found in the body";
            }

            var file = parser.Files.First();
            var extension = Path.GetExtension(file.FileName).ToLower();

            var content = "";
            try {
                content = Encoding.UTF8.GetString(file.Content.ToArray());
            }
            catch (Exception ex) {
                return session.Response.Status(500).Text(ex.Message);
            }

            var name = parser.Fields.Where(p => p.Key == nameof(Post_MapPost_HelloWorld_Controller.User.Name)).FirstOrDefault().Value;

            return new { message = $"{name}, {content}" };
        }

        public class Post_MapPost_HelloWorld_Controller {

            public class User {
                public Guid Id { get; set; }
                public bool Enabled { get; set; }
                public string? Name { get; set; }
                public int Counter { get; set; }
                public DateTime CreatedAt { get; set; }
            }

        }

        #endregion MapPost

        #region RequestAborted

        [Fact]
        public async Task RequestAborted_Should_Be_Cancelled_When_Client_Disconnects_During_Async_Handler() {

            // settings
            int port = PortManager.GetFreePort();
            var handlerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestAbortedCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.Configure(options => {
                options.SocketDisconnectPollInterval = TimeSpan.FromMilliseconds(25);
            });

            server.MapGet("/api/abort", async (HttpSession session) => {
                CancellationToken token = session.RequestAborted;

                handlerStarted.TrySetResult(true);

                try {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (OperationCanceledException) {
                    requestAbortedCancelled.TrySetResult(true);
                    return;
                }

                // si jamais ça n'a pas cancel, on le note quand même
                if (token.IsCancellationRequested) {
                    requestAbortedCancelled.TrySetResult(true);
                }
            });

            await server.StartAsync();

            // client socket raw pour pouvoir fermer brutalement
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            using NetworkStream stream = client.GetStream();

            string request =
                "GET /api/abort HTTP/1.1\r\n" +
                $"Host: {server.Address}:{server.Port}\r\n" +
                "Connection: keep-alive\r\n" +
                "\r\n";

            byte[] requestBytes = System.Text.Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(requestBytes);

            // attendre que le handler soit bien entré
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // fermeture client => le serveur doit annuler RequestAborted
            client.Client.Shutdown(SocketShutdown.Both);
            client.Close();

            bool cancelled = await requestAbortedCancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // asserts
            Check.That(cancelled).IsTrue();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(port);
        }

        [Fact]
        public async Task RequestAborted_Should_Not_Be_Cancelled_If_Client_Stays_Connected() {

            // settings
            int port = PortManager.GetFreePort();
            var tokenState = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.Configure(options => {
                options.SocketDisconnectPollInterval = TimeSpan.FromMilliseconds(25);
            });

            server.MapGet("/api/not-aborted", async (HttpSession session) => {
                CancellationToken token = session.RequestAborted;

                await Task.Delay(150);

                tokenState.TrySetResult(token.IsCancellationRequested);

                await session.Response.Text("OK").SendAsync();
            });

            await server.StartAsync();

            // client
            using var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/not-aborted");
            string content = await response.Content.ReadAsStringAsync();

            bool isCancelled = await tokenState.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("OK");
            Check.That(isCancelled).IsFalse();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(port);
        }

        [Fact]
        public async Task ThrowIfAborted_Should_Throw_When_Client_Disconnects() {

            // settings
            int port = PortManager.GetFreePort();
            var throwDetected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handlerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, port);

            server.Configure(options => {
                options.SocketDisconnectPollInterval = TimeSpan.FromMilliseconds(25);
            });

            server.MapGet("/api/throw-if-aborted", async (HttpSession session) => {
                _ = session.RequestAborted;
                handlerStarted.TrySetResult(true);

                try {
                    while (true) {
                        session.ThrowIfAborted();
                        await Task.Delay(25);
                    }
                }
                catch (OperationCanceledException) {
                    throwDetected.TrySetResult(true);
                }
            });

            await server.StartAsync();

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);

            using NetworkStream stream = client.GetStream();

            string request =
                "GET /api/throw-if-aborted HTTP/1.1\r\n" +
                $"Host: {server.Address}:{server.Port}\r\n" +
                "Connection: keep-alive\r\n" +
                "\r\n";

            byte[] requestBytes = System.Text.Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(requestBytes);

            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            client.Client.Shutdown(SocketShutdown.Both);
            client.Close();

            bool thrown = await throwDetected.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // asserts
            Check.That(thrown).IsTrue();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(port);
        }

        #endregion RequestAborted

        #region Missing user Response

        [Fact]
        public async Task Handler_That_Completes_Without_Sending_Response_Should_Return_500() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapGet("/api/missing-response", (HttpSession session) => {
                // simulate a developer mistake:
                // response is touched, but never actually sent
                session.Response.Text("Hello World").Status(200);
                return Task.CompletedTask;
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/missing-response");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
            Check.That(content).Contains("Internal Server Error");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion Missing user Response

    }

}
