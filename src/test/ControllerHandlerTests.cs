using System.Net;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for Controller handler
    /// </summary>
    public class ControllerHandlerTests {

        #region get_noparameter

        [Fact]
        public async Task Get_DynamicContent_NoParameter_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController< Get_DynamicContent_NoParameter_HelloWorld_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Get_DynamicContent_NoParameter_HelloWorld_Controller : Controller {
            [Route("GET", "/hello")]
            public object HelloWorld() {
                return new { message = "Hello World !" };
            }
        }

        #endregion get_noparameter

        #region get_qs_parameter_name_required

        [Fact]
        public async Task Get_DynamicContent_Qs_ParameterNameRequired_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Get_DynamicContent_Qs_ParameterName_HelloWorld_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_DynamicContent_Qs_ParameterNameRequired_500() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Get_DynamicContent_Qs_ParameterName_HelloWorld_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.InternalServerError);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Get_DynamicContent_Qs_ParameterName_HelloWorld_Controller : Controller {
            [Route("GET", "/hello")]
            public object HelloWorld(string name) {
                return new { message = $"{name}, Hello World !" };
            }
        }

        #endregion get_qs_parameter_name_required

        #region get_qs_parameter_name_optional

        [Fact]
        public async Task Get_DynamicContent_Qs_ParameterNameOptional_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Get_DynamicContent_Qs_ParameterNameOptional_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = ", Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_DynamicContent_Qs_ParameterNameOptionalExists_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Get_DynamicContent_Qs_ParameterNameOptional_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello?name=");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = ", Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_DynamicContent_Qs_ParameterNameOptional_HelloWorldName() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Get_DynamicContent_Qs_ParameterNameOptional_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello?name=Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Get_DynamicContent_Qs_ParameterNameOptional_Controller : Controller {
            [Route("GET", "/hello")]
            public object HelloWorld(string? name = null) {
                return new { message = $"{name}, Hello World !" };
            }
        }

        #endregion get_qs_parameter_name_optional

        #region get_path_parameter_name

        [Fact]
        public async Task Get_DynamicContent_Path_ParameterName_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapController<Get_DynamicContent_Path_ParameterName_HelloWorld_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello/Chris");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Chris, Hello World !" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_DynamicContent_Path_Qs_ParameterName_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.MapController<Get_DynamicContent_Path_ParameterName_HelloWorld_Controller>("/api");

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/test/hello/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Route("/test")]
        public class Get_DynamicContent_Path_ParameterName_HelloWorld_Controller : Controller {
            [Route("GET", "/hello/:name")]
            public object HelloWorld(string name) {
                return new { message = $"{name}, Hello World !" };
            }
        }

        #endregion get_path_parameter_name

        #region post

        [Fact]
        public async Task BodyMap_Raw_DynamicContent_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Post_DynamicContent_HelloWorld_Controller>("/api");
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
        public async Task BodyMap_Json_DynamicContent_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Post_DynamicContent_HelloWorld_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = new Post_DynamicContent_HelloWorld_Controller.User() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
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
        public async Task BodyMap_File_DynamicContent_HelloWorld() {

            string testFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "BodyMap_File_DynamicContent_HelloWorld", "file.txt");
            string testFileContent = "Hello World !";
            Directory.CreateDirectory(Path.Combine(testFilePath, ".."));
            File.WriteAllText(testFilePath, testFileContent);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapController<Post_DynamicContent_HelloWorld_Controller>("/api");
            await server.StartAsync();

            // client
            var client = new HttpClient();
            using var payload = new MultipartFormDataContent();
            using var fileStream = File.OpenRead(testFilePath);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            payload.Add(fileContent, "file", Path.GetFileName(testFilePath));
            var user = new Post_DynamicContent_HelloWorld_Controller.User() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            payload.Add(new StringContent(user.Name), nameof(Post_DynamicContent_HelloWorld_Controller.User.Name));
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/api/test/file", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = $"{user.Name}, {testFileContent}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
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

        #endregion post

    }

}
