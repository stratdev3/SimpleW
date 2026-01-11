using System.Net;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Body
    /// </summary>
    public class RequestBodyTests {

        [Fact]
        public async Task Body_MapPost_Raw() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/api/test/raw", (HttpSession session) => {
                return session.Response.Text(session.Request.BodyString);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = "Chris";
            var payload = new StringContent(user);
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/test/raw", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(user);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Body_MapPost_Json() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/api/test/hello", (HttpSession session) => {
                var user = new Body_MapPost_User();
                session.Request.BodyMap(user);
                return new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = new Body_MapPost_User() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            var json = JsonSerializer.Serialize(user);
            var payload = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/test/hello", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Body_MapPost_File() {

            string testFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "BodyMap_File_DynamicContent_HelloWorld", "file.txt");
            string testFileContent = "Hello World !";
            Directory.CreateDirectory(Path.Combine(testFilePath, ".."));
            File.WriteAllText(testFilePath, testFileContent);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/api/test/file", object (HttpSession session) => {
                var parser = session.Request.BodyMultipart();
                if (parser == null || parser.Files.Count == 0) {
                    return "no file found in the body";
                }

                var file = parser.Files.First();
                var extension = Path.GetExtension(file.FileName).ToLower();

                var content = "";
                try {
                    content = Encoding.UTF8.GetString(file.Content.ToArray());
                }
                catch (Exception ex) {
                    return session.Response.Status(500).Json(ex.Message);
                }

                var name = parser.Fields.Where(p => p.Key == nameof(Body_MapPost_User.Name)).FirstOrDefault().Value;

                return new { message = $"{name}, {content}" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            using var payload = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(testFilePath);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            payload.Add(fileContent, "file", Path.GetFileName(testFilePath));
            var user = new Body_MapPost_User() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            payload.Add(new StringContent(user.Name), nameof(Body_MapPost_User.Name));
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/test/file", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = $"{user.Name}, {testFileContent}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        public class Body_MapPost_User {
            public Guid Id { get; set; }
            public bool Enabled { get; set; }
            public string? Name { get; set; }
            public int Counter { get; set; }
            public DateTime CreatedAt { get; set; }
        }

    }

}
