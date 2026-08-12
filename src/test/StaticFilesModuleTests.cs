using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
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
        public async Task Get_StaticContent_Should_Return_Forbidden_When_Authorize_Denies() {

            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseStaticFilesModule(options => {
                options.Path = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)!;
                options.Prefix = "/files";
                options.Authorize = _ => false;
            });

            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = new HttpClient();
                using var response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/SimpleW.dll");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                if (started) {
                    await server.StopAsync();
                }
            }
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_NoIndex_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_Index_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_DefaultDocument_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_NoCache_DefaultDocumentMaintenance_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        #endregion no_cache

        #region cache

        #region file exists

        [Fact]
        public async Task Get_StaticContent_NoIndex_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Index_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        #endregion file exists

        #region proper header

        [Fact]
        public async Task Get_StaticContent_Cache_File_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        #endregion proper header

        #region compressed cache

        [Fact]
        public async Task Get_StaticContent_Cache_Compressed_Gzip_200() {

            string path = CreateStaticFilesTestDirectory(nameof(Get_StaticContent_Cache_Compressed_Gzip_200));
            string expected = "gzip:" + new string('a', 4096);
            File.WriteAllText(Path.Combine(path, "app.txt"), expected);

            var server = CreateCachedStaticServer(path);
            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = CreateRawClient();
                using var response = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "app.txt"), "gzip");
                string content = await ReadDecodedContentAsync(response);

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");
                Check.That(response.Headers.Vary).Contains("Accept-Encoding");
                Check.That(content).IsEqualTo(expected);
            }
            finally {
                await StopIfStartedAsync(server, started);
                TryDeleteDirectory(path);
            }
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Compressed_Gzip_Hit_200() {

            string path = CreateStaticFilesTestDirectory(nameof(Get_StaticContent_Cache_Compressed_Gzip_Hit_200));
            string expected = "gzip-hit:" + new string('b', 4096);
            File.WriteAllText(Path.Combine(path, "app.txt"), expected);

            var server = CreateCachedStaticServer(path);
            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = CreateRawClient();
                using var response1 = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "app.txt"), "gzip");
                string content1 = await ReadDecodedContentAsync(response1);
                using var response2 = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "app.txt"), "gzip");
                string content2 = await ReadDecodedContentAsync(response2);

                Check.That(response1.StatusCode).Is(HttpStatusCode.OK);
                Check.That(response2.StatusCode).Is(HttpStatusCode.OK);
                Check.That(response1.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");
                Check.That(response2.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");
                Check.That(content1).IsEqualTo(expected);
                Check.That(content2).IsEqualTo(expected);
            }
            finally {
                await StopIfStartedAsync(server, started);
                TryDeleteDirectory(path);
            }
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Compressed_Brotli_When_Preferred_200() {

            string path = CreateStaticFilesTestDirectory(nameof(Get_StaticContent_Cache_Compressed_Brotli_When_Preferred_200));
            string expected = "brotli:" + new string('c', 4096);
            File.WriteAllText(Path.Combine(path, "app.txt"), expected);

            var server = CreateCachedStaticServer(path);
            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = CreateRawClient();
                using var response = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "app.txt"), "gzip;q=0.5, br;q=1.0");
                string content = await ReadDecodedContentAsync(response);

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("br");
                Check.That(content).IsEqualTo(expected);
            }
            finally {
                await StopIfStartedAsync(server, started);
                TryDeleteDirectory(path);
            }
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Does_Not_Compress_When_Not_Eligible() {

            string path = CreateStaticFilesTestDirectory(nameof(Get_StaticContent_Cache_Does_Not_Compress_When_Not_Eligible));
            byte[] image = Enumerable.Range(0, 2048).Select(i => (byte)(i % 251)).ToArray();
            string small = "tiny";
            string large = "large:" + new string('d', 4096);
            File.WriteAllBytes(Path.Combine(path, "image.png"), image);
            File.WriteAllText(Path.Combine(path, "small.txt"), small);
            File.WriteAllText(Path.Combine(path, "large.txt"), large);

            var server = CreateCachedStaticServer(path);
            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = CreateRawClient();
                using var imageResponse = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "image.png"), "gzip");
                byte[] imageContent = await imageResponse.Content.ReadAsByteArrayAsync();
                using var smallResponse = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "small.txt"), "gzip");
                string smallContent = await smallResponse.Content.ReadAsStringAsync();
                using var unknownResponse = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "large.txt"), "zstd");
                string unknownContent = await unknownResponse.Content.ReadAsStringAsync();

                Check.That(imageResponse.Content.Headers.ContentEncoding).IsEmpty();
                Check.That(imageContent.SequenceEqual(image)).IsTrue();
                Check.That(smallResponse.Content.Headers.ContentEncoding).IsEmpty();
                Check.That(smallContent).IsEqualTo(small);
                Check.That(unknownResponse.Content.Headers.ContentEncoding).IsEmpty();
                Check.That(unknownContent).IsEqualTo(large);
            }
            finally {
                await StopIfStartedAsync(server, started);
                TryDeleteDirectory(path);
            }
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Compressed_Validators_Return_304() {

            string path = CreateStaticFilesTestDirectory(nameof(Get_StaticContent_Cache_Compressed_Validators_Return_304));
            string expected = "validators:" + new string('e', 4096);
            File.WriteAllText(Path.Combine(path, "app.txt"), expected);

            var server = CreateCachedStaticServer(path);
            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = CreateRawClient();
                using var warmResponse = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "app.txt"), "gzip");
                string warmContent = await ReadDecodedContentAsync(warmResponse);
                string etag = warmResponse.Headers.ETag!.Tag!;
                warmResponse.Content.Headers.TryGetValues("Last-Modified", out var modifiedSinces);
                string lastModified = modifiedSinces!.First();

                using var etagRequest = new HttpRequestMessage(HttpMethod.Get, StaticUrl(server, "app.txt"));
                etagRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
                etagRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);
                using var etagResponse = await client.SendAsync(etagRequest);

                using var modifiedRequest = new HttpRequestMessage(HttpMethod.Get, StaticUrl(server, "app.txt"));
                modifiedRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip");
                modifiedRequest.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
                using var modifiedResponse = await client.SendAsync(modifiedRequest);

                Check.That(warmContent).IsEqualTo(expected);
                Check.That(etagResponse.StatusCode).Is(HttpStatusCode.NotModified);
                Check.That(etagResponse.Content.Headers.ContentEncoding).IsEmpty();
                Check.That(modifiedResponse.StatusCode).Is(HttpStatusCode.NotModified);
                Check.That(modifiedResponse.Content.Headers.ContentEncoding).IsEmpty();
            }
            finally {
                await StopIfStartedAsync(server, started);
                TryDeleteDirectory(path);
            }
        }

        [Fact]
        public async Task Get_StaticContent_Cache_Compressed_File_Modified_ReturnsUpdatedContent() {

            string path = CreateStaticFilesTestDirectory(nameof(Get_StaticContent_Cache_Compressed_File_Modified_ReturnsUpdatedContent));
            string filePath = Path.Combine(path, "app.txt");
            string v1 = "v1:" + new string('f', 4096);
            string v2 = "v2:" + new string('g', 5000);
            File.WriteAllText(filePath, v1);

            var server = CreateCachedStaticServer(path);
            bool started = false;
            try {
                await server.StartAsync();
                started = true;

                using var client = CreateRawClient();
                using var warmResponse = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "app.txt"), "gzip");
                string warmContent = await ReadDecodedContentAsync(warmResponse);

                File.WriteAllText(filePath, v2);

                string updatedContent = string.Empty;
                for (int i = 0; i < 20; i++) {
                    await Task.Delay(100);
                    using var response = await GetWithAcceptEncodingAsync(client, StaticUrl(server, "app.txt"), "gzip");
                    updatedContent = await ReadDecodedContentAsync(response);
                    if (updatedContent == v2) {
                        break;
                    }
                }

                Check.That(warmContent).IsEqualTo(v1);
                Check.That(updatedContent).IsEqualTo(v2);
            }
            finally {
                await StopIfStartedAsync(server, started);
                TryDeleteDirectory(path);
            }
        }

        #endregion compressed cache

        #region hit 304

        [Fact]
        public async Task Get_StaticContent_Cache_File_Hit_Etag_304() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Hit_ModifiedSince_304() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        #endregion hit 304

        #region miss 200

        [Fact]
        public async Task Get_StaticContent_Cache_File_Miss_Etag_304() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Miss_ModifiedSince_304() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        #endregion miss 200

        #region default document

        [Fact]
        public async Task Get_StaticContent_Cache_DefaultDocument_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_Cache_DefaultDocumentMaintenance_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        #endregion default document

        #endregion cache

        #region watcher

        [Fact]
        public async Task Get_StaticContent_Cache_File_Modified_ReturnsUpdatedContent() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Deleted_Returns404() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        [Fact]
        public async Task Get_StaticContent_Cache_File_Created_Returns200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, 0);

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
        }

        #endregion watcher

        private static SimpleWServer CreateCachedStaticServer(string path, Action<StaticFilesModuleExtension.StaticFilesOptions>? configure = null) {
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseStaticFilesModule(options => {
                options.Path = path;
                options.Prefix = "/files";
                options.CacheTimeout = TimeSpan.FromDays(1);
                configure?.Invoke(options);
            });
            return server;
        }

        private static HttpClient CreateRawClient() {
            return new HttpClient(new HttpClientHandler {
                AutomaticDecompression = DecompressionMethods.None
            });
        }

        private static async Task<HttpResponseMessage> GetWithAcceptEncodingAsync(HttpClient client, string url, string acceptEncoding) {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
            return await client.SendAsync(request);
        }

        private static async Task<string> ReadDecodedContentAsync(HttpResponseMessage response) {
            byte[] payload = await response.Content.ReadAsByteArrayAsync();
            string? encoding = response.Content.Headers.ContentEncoding.FirstOrDefault();
            if (encoding == null) {
                return Encoding.UTF8.GetString(payload);
            }

            using var input = new MemoryStream(payload);
            using Stream decoded = encoding switch {
                "gzip" => new GZipStream(input, CompressionMode.Decompress),
                "deflate" => new DeflateStream(input, CompressionMode.Decompress),
                "br" => new BrotliStream(input, CompressionMode.Decompress),
                _ => throw new InvalidOperationException($"Unsupported content encoding '{encoding}'.")
            };
            using var reader = new StreamReader(decoded, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        private static string CreateStaticFilesTestDirectory(string testName) {
            string root = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string path = Path.Combine(root, $"{testName}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string StaticUrl(SimpleWServer server, string fileName) {
            return $"http://{server.Address}:{server.Port}/files/{fileName}";
        }

        private static async Task StopIfStartedAsync(SimpleWServer server, bool started) {
            if (started) {
                await server.StopAsync();
            }
        }

        private static void TryDeleteDirectory(string path) {
            try {
                if (Directory.Exists(path)) {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch { }
        }

    }

}
