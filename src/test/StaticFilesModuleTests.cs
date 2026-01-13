using System.IO;
using System.Net;
using NFluent;
using SimpleW;
using SimpleW.Modules;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for static content
    /// </summary>
    public class StaticFilesModuleTests {

        #region no_cache

        [Fact]
        public async Task Get_StaticContent_NoCache_NoIndex_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                // options.AutoIndex = false; // default
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                // options.AutoIndex = false; // default
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/noexists.txt");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_Index_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                options.AutoIndex = true;
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).Contains("Index of /files");
            Check.That(content).Contains("SimpleW.dll");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                options.AutoIndex = true;
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_DefaultDocument_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_NoCache_DefaultDocument_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "index.html"), "index");

            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                // options.DefaultDocument = "index.html"; // default
            });

            // default document is index.html

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("index");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_DefaultDocumentMaintenance_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_NoCache_DefaultDocumentMaintenance_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "maintenance.html"), "maintenance");
            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                options.DefaultDocument = "maintenance.html";
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("maintenance");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion no_cache

        #region cache

        [Fact]
        public async Task Get_StaticContent_NoIndex_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
                // options.AutoIndex = false; // default
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
                // options.AutoIndex = false; // default
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/noexists.txt");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Index_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
                options.AutoIndex = true;
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).Contains("Index of /files");
            Check.That(content).Contains("SimpleW.dll");
            //Check.That(response.Headers.Contains("Last-Modified")).IsTrue();
            //Check.That(response.Headers.Contains("Cache-Control")).IsTrue();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
                options.AutoIndex = true;
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_DefaultDocument_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_Cache_DefaultDocument_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "index.html"), "index");
            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
                //options.DefaultDocument = "index.html"; // default
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("index");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_DefaultDocumentMaintenance_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_Cache_DefaultDocumentMaintenance_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "maintenance.html"), "maintenance");
            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
                options.DefaultDocument = "maintenance.html";
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("maintenance");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        /*

        [Fact]
        public async Task Get_StaticContent_Cache_NoIndex_Filter_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*", timeout: TimeSpan.FromDays(1));

            // enable autoindex
            server.AutoIndex = true;

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).Contains("Index of /files");
            Check.That(content).Contains("SimpleW.dll");
            Check.That(content).DoesNotContain("OpenTelemetry.dll");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Filter_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*", timeout: TimeSpan.FromDays(1));

            // enable autoindex
            server.AutoIndex = true;

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Filter_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*", timeout: TimeSpan.FromDays(1));

            // enable autoindex
            server.AutoIndex = true;

            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/OpenTelemetry.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        */

        #endregion cache

    }

}
