using System.Net;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Inline Funct
    /// </summary>
    public class InlineFuncTests {

        #region MapGet

        [Fact]
        public async Task MapGet_NoParameter_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterName_NameHelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string? name = null) => {
                return new { message = $"{name}, Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterName_Null1HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string? name = null) => {
                return new { message = $"{name}, Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = ", Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterName_Null2HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string? name = null) => {
                return new { message = $"{name}, Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = ", Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterSessionRequest_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                Check.That(Session).IsNotNull();
                Check.That(Request).IsNotNull();
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterSessionRequestName_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request, string? name = null) => {
                Check.That(Session).IsNotNull();
                Check.That(Request).IsNotNull();
                return new { message = $"{name}, Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task MapGet_ParameterNameRequestSession_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (string name, ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                Check.That(Session).IsNotNull();
                Check.That(Request).IsNotNull();
                return new { message = $"{name}, Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        #endregion MapGet

        #region MapPost

        [Fact]
        public async Task BodyMap_Raw_MapPost_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                return Raw(Session, Request);
            });
            server.Start();

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
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task BodyMap_formurlencoded_MapPost_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                return HelloWorld(Session, Request);
            });
            server.Start();

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
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task BodyMap_Json_MapPost_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                return HelloWorld(Session, Request);
            });
            server.Start();

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
            server.Stop();
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
            server.MapPost("/", (ISimpleWSession Session, NetCoreServer.HttpRequest Request) => {
                return FileResponse(Session, Request);
            });
            server.Start();

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
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        public object HelloWorld(ISimpleWSession Session, NetCoreServer.HttpRequest Request) {
            var user = new Post_MapPost_HelloWorld_Controller.User();
            Request.BodyMap(user);

            return new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" };
        }

        public object Raw(ISimpleWSession Session, NetCoreServer.HttpRequest Request) {
            return Session.Response.MakeGetResponse(Request.Body);
        }

        public object FileResponse(ISimpleWSession Session, NetCoreServer.HttpRequest Request) {

            var parser = Request.BodyFile();
            if (!parser.Files.Any(f => f.Data.Length >= 0)) {
                return "no file found in the body";
            }

            var file = parser.Files.First();
            var extension = Path.GetExtension(file.FileName).ToLower();

            var content = "";
            using (var ms = new MemoryStream()) {
                try {
                    file.Data.CopyTo(ms);
                    content = Encoding.UTF8.GetString(ms.ToArray());
                }
                catch (Exception ex) {
                    return Session.Response.MakeInternalServerErrorResponse(ex.Message);
                }
            }

            var name = parser.Parameters.Where(p => p.Name == nameof(Post_MapPost_HelloWorld_Controller.User.Name)).FirstOrDefault()?.Data;

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

    }

}
