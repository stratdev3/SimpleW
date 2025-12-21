using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NFluent;
using SimpleW;
using Xunit;
using static test.DynamicContentTests;


namespace test {

    /// <summary>
    /// Tests for static content
    /// </summary>
    public class StaticContentTests {

        #region no_cache

        [Fact]
        public async Task Get_StaticContent_NoCache_NoIndex_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // autoindex default is false

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // autoindex default is false

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files");

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/noexists.txt");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_Index_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files");

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).Contains("Index of /files");
            Check.That(content).Contains("SimpleW.dll");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files");

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_DefaultDocument_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_NoCache_DefaultDocument_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "index.html"), "index");
            server.AddStaticContent(path, "/files");

            // default document is index.html

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("index");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_DefaultDocumentMaintenance_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_NoCache_DefaultDocumentMaintenance_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "maintenance.html"), "maintenance");
            server.AddStaticContent(path, "/files");

            // change default document
            server.DefaultDocument = "maintenance.html";

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("maintenance");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_Index_Filter_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*");

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

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
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_Filter_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*");

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_Filter_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*");

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/OpenTelemetry.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        #endregion no_cache

        #region cache

        [Fact]
        public async Task Get_StaticContent_NoIndex_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // autoindex default is false

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", timeout: TimeSpan.FromDays(1));

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            // autoindex default is false

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", timeout: TimeSpan.FromDays(1));

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/noexists.txt");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Index_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", timeout: TimeSpan.FromDays(1));

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).Contains("Index of /files");
            Check.That(content).Contains("SimpleW.dll");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", timeout: TimeSpan.FromDays(1));

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_DefaultDocument_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_Cache_DefaultDocument_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "index.html"), "index");
            server.AddStaticContent(path, "/files", timeout: TimeSpan.FromDays(1));

            // default document is index.html

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("index");

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_DefaultDocumentMaintenance_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_Cache_DefaultDocumentMaintenance_200");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "maintenance.html"), "maintenance");
            server.AddStaticContent(path, "/files", timeout: TimeSpan.FromDays(1));

            // change default document
            server.DefaultDocument = "maintenance.html";

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo("maintenance");

            // dispose
            server.Stop();
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

            server.Start();

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
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Filter_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*", timeout: TimeSpan.FromDays(1));

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Filter_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.AddStaticContent(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "/files", "SimpleW.*", timeout: TimeSpan.FromDays(1));

            // enable autoindex
            server.AutoIndex = true;

            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/OpenTelemetry.dll");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        */

        #endregion cache

    }

}
