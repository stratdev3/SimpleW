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

        #region file exists

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
            Check.That(response.Content.Headers.Contains("Last-Modified")).IsTrue();
            Check.That(response.Headers.CacheControl?.NoCache ?? false).IsTrue();
            Check.That(content).Contains("Index of /files");
            Check.That(content).Contains("SimpleW.xml");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion file exists

        #region proper header

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
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(response.Content.Headers.Contains("Last-Modified")).IsTrue();
            Check.That(response.Headers?.CacheControl?.MaxAge).IsNotNull();
            Check.That(response.Headers?.ETag?.Tag).IsNotNull();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion proper header

        #region hit 304

        [Fact]
        public async Task Get_StaticContent_Cache_File_Hit_Etag_304() {

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
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            var content = await response.Content.ReadAsStringAsync();
            var etag = response.Headers?.ETag?.Tag;

            // client should it cache
            var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            request2.Headers.Add("If-None-Match", etag);
            var response2 = await client.SendAsync(request2);
            var content2 = await response2.Content.ReadAsStringAsync();

            // asserts
            Check.That(response2.StatusCode).Is(HttpStatusCode.NotModified);
            Check.That(response2.Content.Headers.Contains("Last-Modified")).IsTrue();
            Check.That(response2.Headers?.CacheControl?.MaxAge).IsNotNull();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Hit_ModifiedSince_304() {

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
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            var content = await response.Content.ReadAsStringAsync();
            response.Content.Headers.TryGetValues("Last-Modified", out var modifiedSinces);

            // client should it cache
            var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            request2.Headers.Add("If-Modified-Since", modifiedSinces?.First());
            var response2 = await client.SendAsync(request2);
            var content2 = await response2.Content.ReadAsStringAsync();

            // asserts
            Check.That(response2.StatusCode).Is(HttpStatusCode.NotModified);
            Check.That(response2.Content.Headers.Contains("Last-Modified")).IsTrue();
            Check.That(response2.Headers?.CacheControl?.MaxAge).IsNotNull();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion hit 304

        #region miss 200

        [Fact]
        public async Task Get_StaticContent_Cache_File_Miss_Etag_304() {

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
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            var content = await response.Content.ReadAsStringAsync();
            var etag = response.Headers?.ETag?.Tag;

            // client should it cache
            var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            request2.Headers.Add("If-None-Match", "\"000000-639038643743428280\"");
            var response2 = await client.SendAsync(request2);
            var content2 = await response2.Content.ReadAsStringAsync();

            // asserts
            Check.That(response2.StatusCode).Is(HttpStatusCode.OK);
            Check.That(response2.Content.Headers.Contains("Last-Modified")).IsTrue();
            Check.That(response2.Headers?.CacheControl?.MaxAge).IsNotNull();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Miss_ModifiedSince_304() {

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
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            var content = await response.Content.ReadAsStringAsync();
            response.Content.Headers.TryGetValues("Last-Modified", out var modifiedSinces);

            // client should it cache
            var request2 = new HttpRequestMessage(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/SimpleW.xml");
            request2.Headers.Add("If-Modified-Since", "Mon, 12 Jan 2026 23:27:48 GMT");
            var response2 = await client.SendAsync(request2);
            var content2 = await response2.Content.ReadAsStringAsync();

            // asserts
            Check.That(response2.StatusCode).Is(HttpStatusCode.OK);
            Check.That(response2.Content.Headers.Contains("Last-Modified")).IsTrue();
            Check.That(response2.Headers?.CacheControl?.MaxAge).IsNotNull();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion miss 200

        #region default document

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

        #endregion default document

        #endregion cache

        #region watcher

        [Fact]
        public async Task Get_StaticContent_Cache_File_Modified_ReturnsUpdatedContent() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_Cache_File_Modified_ReturnsUpdatedContent");
            Directory.CreateDirectory(path);

            string filePath = Path.Combine(path, "test.txt");
            File.WriteAllText(filePath, "v1");

            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();

            // warm cache
            var response1 = await client.GetAsync($"http://{server.Address}:{server.Port}/files/test.txt");
            var content1 = await response1.Content.ReadAsStringAsync();

            Check.That(response1.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content1).IsEqualTo("v1");

            // modify file
            File.WriteAllText(filePath, "v2");

            // let watcher invalidate cache (best effort)
            await Task.Delay(150);

            // should return updated content (not cached v1)
            var response2 = await client.GetAsync($"http://{server.Address}:{server.Port}/files/test.txt");
            var content2 = await response2.Content.ReadAsStringAsync();

            Check.That(response2.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content2).IsEqualTo("v2");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Deleted_Returns404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_Cache_File_Deleted_Returns404");
            Directory.CreateDirectory(path);

            string filePath = Path.Combine(path, "test.txt");
            File.WriteAllText(filePath, "hello");

            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();

            // warm cache
            var response1 = await client.GetAsync($"http://{server.Address}:{server.Port}/files/test.txt");
            var content1 = await response1.Content.ReadAsStringAsync();

            Check.That(response1.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content1).IsEqualTo("hello");

            // delete file
            File.Delete(filePath);

            // let watcher invalidate cache
            await Task.Delay(150);

            // should now return 404
            var response2 = await client.GetAsync($"http://{server.Address}:{server.Port}/files/test.txt");
            var content2 = await response2.Content.ReadAsStringAsync();

            Check.That(response2.StatusCode).Is(HttpStatusCode.NotFound);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Created_Returns200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            string path = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "Get_StaticContent_Cache_File_Created_Returns200");
            Directory.CreateDirectory(path);

            // important: file does NOT exist at startup
            string fileName = $"new-{Guid.NewGuid()}.txt";
            string filePath = Path.Combine(path, fileName);

            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();

            // file doesn't exist yet => 404
            var response1 = await client.GetAsync($"http://{server.Address}:{server.Port}/files/{fileName}");
            Check.That(response1.StatusCode).Is(HttpStatusCode.NotFound);

            // create file
            File.WriteAllText(filePath, "new content");

            // let watcher invalidate kind-cache/missing cache
            await Task.Delay(150);

            // now should return 200 + content
            var response2 = await client.GetAsync($"http://{server.Address}:{server.Port}/files/{fileName}");
            var content2 = await response2.Content.ReadAsStringAsync();

            Check.That(response2.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content2).IsEqualTo("new content");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion watcher

    }

}
