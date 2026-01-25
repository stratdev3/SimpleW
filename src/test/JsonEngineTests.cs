using System.Net;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.JsonEngine.Newtonsoft;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for JsonEngine
    /// </summary>
    public class JsonEngineTests {

        #region systemTextJson

        [Fact]
        public async Task Get_SystemTextJson_Serialization_HelloWorld() {

            int i = 1;
            string message = "Hello World";
            DateOnly date = DateOnly.FromDateTime(DateTime.Now);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => {
                return new { i, message, date };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { i, message, date }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Post_SystemTextJson_Deserialization_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapPost("/", (HttpSession session) => {
                var user = new User_JsonEngineTests();
                session.Request.BodyMap(user);

                return new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = new User_JsonEngineTests() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            var json = JsonSerializer.Serialize(user);
            var payload = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion systemTextJson

        #region newtonSoft

        [Fact]
        public async Task Get_NewtonSoft_Serialization_HelloWorld() {

            int i = 1;
            string message = "Hello World";
            DateOnly date = DateOnly.FromDateTime(DateTime.Now);

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.ConfigureJsonEngine(new NewtonsoftJsonEngine());
            server.MapGet("/", () => {
                return new { i, message, date };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(Newtonsoft.Json.JsonConvert.SerializeObject(new { i, message, date }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Post_NewtonSoft_Deserialization_HelloWorld() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.ConfigureJsonEngine(new NewtonsoftJsonEngine());
            server.MapPost("/", (HttpSession session) => {
                var user = new User_JsonEngineTests();
                session.Request.BodyMap(user);

                return new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var user = new User_JsonEngineTests() { Id = Guid.NewGuid(), Name = "Chris", Counter = 0, CreatedAt = DateTime.Now, Enabled = true };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(user);
            var payload = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"http://{server.Address}:{server.Port}/", payload);
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(Newtonsoft.Json.JsonConvert.SerializeObject(new { message = $"{user.Name}, Hello World ! It's {user.CreatedAt.ToLongDateString()}" }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion newtonSoft

        public class User_JsonEngineTests {
            public Guid Id { get; set; }
            public bool Enabled { get; set; }
            public string? Name { get; set; }
            public int Counter { get; set; }
            public DateTime CreatedAt { get; set; }
        }

    }

}
