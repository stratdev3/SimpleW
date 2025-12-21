using System.Net;
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

        //[Fact]
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

    }

}
