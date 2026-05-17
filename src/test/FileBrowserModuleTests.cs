using System.Net;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.JsonEngine.Newtonsoft;
using SimpleW.Service.FileBrowser;
using Xunit;

namespace test {

    /// <summary>
    /// Tests for FileBrowserModule.
    /// </summary>
    public class FileBrowserModuleTests {

        [Theory]
        [InlineData("SimpleW.Service.FileBrowser.Client.index.html")]
        [InlineData("SimpleW.Service.FileBrowser.Client.app.js")]
        [InlineData("SimpleW.Service.FileBrowser.Client.styles.css")]
        public void Ui_Client_Should_Be_Embedded_In_Assembly(string resourceName) {
            using Stream? resource = typeof(FileBrowserOptions).Assembly.GetManifestResourceStream(resourceName);
            Check.That(resource).IsNotNull();
        }

        [Fact]
        public void UseFileBrowserModule_Should_Require_Explicit_Authorization() {
            string root = CreateRoot(nameof(UseFileBrowserModule_Should_Require_Explicit_Authorization));
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            Check.ThatCode(() => {
                server.UseFileBrowserModule(options => {
                    options.Path = root;
                    options.Prefix = "/files";
                });
            }).Throws<ArgumentException>();
        }

        [Fact]
        public async Task List_Should_Return_Forbidden_When_Authorize_Denies() {
            string root = CreateRoot(nameof(List_Should_Return_Forbidden_When_Authorize_Denies));
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseFileBrowserModule(options => {
                options.Path = root;
                options.Prefix = "/files";
                options.Authorize = _ => false;
                options.ServeUi = false;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);

                HttpResponseMessage eventsResponse = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/events");
                Check.That(eventsResponse.StatusCode).Is(HttpStatusCode.Forbidden);

                HttpResponseMessage cancelResponse = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/operations/cancel",
                    JsonContent(new { })
                );
                Check.That(cancelResponse.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task List_Should_Return_Items_And_Exclude_Internal_Directories() {
            string root = CreateRoot(nameof(List_Should_Return_Items_And_Exclude_Internal_Directories));
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            Directory.CreateDirectory(Path.Combine(root, ".trash"));
            File.WriteAllText(Path.Combine(root, "readme.txt"), "hello");

            var server = CreateAnonymousServer(root, 0)
                .ConfigureJsonEngine(new NewtonsoftJsonEngine());

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list");
                using JsonDocument json = await ReadJsonAsync(response);

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                JsonElement items = json.RootElement.GetProperty("items");
                string content = items.ToString();
                Check.That(content).Contains("docs");
                Check.That(content).Contains("readme.txt");
                Check.That(content).DoesNotContain(".trash");

                foreach (JsonElement item in items.EnumerateArray()) {
                    Check.That(item.TryGetProperty("name", out _)).IsTrue();
                    Check.That(item.TryGetProperty("path", out _)).IsTrue();
                    Check.That(item.TryGetProperty("type", out _)).IsTrue();
                    Check.That(item.TryGetProperty("size", out _)).IsTrue();
                    Check.That(item.TryGetProperty("modifiedUtc", out _)).IsTrue();
                    Check.That(item.TryGetProperty("Name", out _)).IsFalse();
                    Check.That(item.TryGetProperty("ModifiedUtc", out _)).IsFalse();
                }
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Ui_Should_Serve_Static_Client_And_Keep_Api_Routes() {
            string root = CreateRoot(nameof(Ui_Should_Serve_Static_Client_And_Keep_Api_Routes));
            File.WriteAllText(Path.Combine(root, "readme.txt"), "hello");

            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage ui = await client.GetAsync($"http://{server.Address}:{server.Port}/files");
                string html = await ui.Content.ReadAsStringAsync();
                Check.That(ui.StatusCode).Is(HttpStatusCode.OK);
                Check.That(html).Contains("SimpleW File Browser");
                Check.That(html).Contains("id=\"uploadModal\"");
                Check.That(html).Contains("id=\"toggleOperations\"");
                Check.That(html).Contains("id=\"operationCount\"");
                Check.That(html).Contains("id=\"operationsPanel\" class=\"operations-panel\" hidden");
                Check.That(html).Contains("id=\"clearOperations\"");
                Check.That(html).Contains("id=\"cancelOperations\"");
                Check.That(html).Contains("<h2>Operations</h2>");
                Check.That(html).DoesNotContain("id=\"drop\"");
                Check.That(html).DoesNotContain("id=\"queue\"");

                HttpResponseMessage css = await client.GetAsync($"http://{server.Address}:{server.Port}/files/styles.css");
                string cssText = await css.Content.ReadAsStringAsync();
                Check.That(css.StatusCode).Is(HttpStatusCode.OK);
                Check.That(css.Content.Headers.ContentType?.MediaType).IsEqualTo("text/css");
                Check.That(cssText).Contains(".shell");

                HttpResponseMessage js = await client.GetAsync($"http://{server.Address}:{server.Port}/files/app.js");
                string jsText = await js.Content.ReadAsStringAsync();
                Check.That(js.StatusCode).Is(HttpStatusCode.OK);
                Check.That(js.Content.Headers.ContentType?.MediaType).IsEqualTo("text/javascript");
                Check.That(jsText).Contains("loadConfig");
                Check.That(jsText).Contains("uploadModal");
                Check.That(jsText).Contains("trackQueuedOperation");
                Check.That(jsText).Contains("cancelAllOperations");
                Check.That(jsText).Contains("clearOperationHistory");
                Check.That(jsText).Contains("filebrowser.operation.cancelled");
                Check.That(jsText).Contains("filebrowser.upload.cancelled");
                Check.That(jsText).DoesNotContain("renderQueue");

                string? etag = js.Headers.ETag?.Tag;
                Check.That(etag).IsNotNull();
                Check.That(js.Headers.CacheControl).IsNotNull();
                Check.That(js.Headers.CacheControl!.NoCache).IsTrue();

                using HttpRequestMessage headRequest = new(HttpMethod.Head, $"http://{server.Address}:{server.Port}/files/app.js");
                using HttpResponseMessage head = await client.SendAsync(headRequest);
                Check.That(head.StatusCode).Is(HttpStatusCode.OK);
                Check.That(head.Content.Headers.ContentLength).IsEqualTo((long)Encoding.UTF8.GetByteCount(jsText));
                Check.That((await head.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);

                using HttpRequestMessage conditionalRequest = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/app.js");
                conditionalRequest.Headers.TryAddWithoutValidation("If-None-Match", etag!);
                using HttpResponseMessage notModified = await client.SendAsync(conditionalRequest);
                Check.That(notModified.StatusCode).Is(HttpStatusCode.NotModified);

                HttpResponseMessage config = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/config");
                using JsonDocument configJson = await ReadJsonAsync(config);
                Check.That(config.StatusCode).Is(HttpStatusCode.OK);
                Check.That(configJson.RootElement.GetProperty("prefix").GetString()).IsEqualTo("/files");
                Check.That(configJson.RootElement.GetProperty("apiPrefix").GetString()).IsEqualTo("/files/api");

                HttpResponseMessage list = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list");
                using JsonDocument listJson = await ReadJsonAsync(list);
                Check.That(list.StatusCode).Is(HttpStatusCode.OK);
                Check.That(listJson.RootElement.GetProperty("items").ToString()).Contains("readme.txt");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Ui_Root_Should_Redirect_To_Trailing_Slash_For_Relative_Assets() {
            string root = CreateRoot(nameof(Ui_Root_Should_Redirect_To_Trailing_Slash_For_Relative_Assets));
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClientHandler handler = new() {
                    AllowAutoRedirect = false
                };
                using HttpClient client = new(handler);

                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/files");

                Check.That(response.StatusCode).Is(HttpStatusCode.Found);
                Check.That(response.Headers.Location?.ToString()).IsEqualTo("/files/");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Ui_Should_Serve_At_Root_Prefix() {
            string root = CreateRoot(nameof(Ui_Should_Serve_At_Root_Prefix));
            var server = CreateAnonymousServer(root, 0, options => {
                options.Prefix = "/";
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage ui = await client.GetAsync($"http://{server.Address}:{server.Port}/");
                string html = await ui.Content.ReadAsStringAsync();
                Check.That(ui.StatusCode).Is(HttpStatusCode.OK);
                Check.That(html).Contains("SimpleW File Browser");

                HttpResponseMessage js = await client.GetAsync($"http://{server.Address}:{server.Port}/app.js");
                Check.That(js.StatusCode).Is(HttpStatusCode.OK);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task ServeUi_False_Should_Not_Map_Client_Routes() {
            string root = CreateRoot(nameof(ServeUi_False_Should_Not_Map_Client_Routes));
            var server = CreateAnonymousServer(root, 0, options => {
                options.ServeUi = false;
                options.EnableEvents = false;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage ui = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
                Check.That(ui.StatusCode).Is(HttpStatusCode.NotFound);

                HttpResponseMessage config = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/config");
                Check.That(config.StatusCode).Is(HttpStatusCode.OK);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Ui_Static_Client_Should_Return_Forbidden_When_Authorize_Denies() {
            string root = CreateRoot(nameof(Ui_Static_Client_Should_Return_Forbidden_When_Authorize_Denies));
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseFileBrowserModule(options => {
                options.Path = root;
                options.Prefix = "/files";
                options.Authorize = _ => false;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage ui = await client.GetAsync($"http://{server.Address}:{server.Port}/files");
                Check.That(ui.StatusCode).Is(HttpStatusCode.Forbidden);

                HttpResponseMessage js = await client.GetAsync($"http://{server.Address}:{server.Port}/files/app.js");
                Check.That(js.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Ui_Should_Use_And_Reload_Explicit_ClientPath() {
            string root = CreateRoot(nameof(Ui_Should_Use_And_Reload_Explicit_ClientPath));
            string clientRoot = Path.Combine(root, "custom-client");
            Directory.CreateDirectory(clientRoot);
            string indexPath = Path.Combine(clientRoot, "index.html");
            File.WriteAllText(indexPath, "<!doctype html><title>custom-client-v1</title>");
            File.WriteAllText(Path.Combine(clientRoot, "styles.css"), "body{margin:0}");
            File.WriteAllText(Path.Combine(clientRoot, "app.js"), "console.log('custom-client');");

            var server = CreateAnonymousServer(root, 0, options => {
                options.ClientPath = clientRoot;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage ui = await client.GetAsync($"http://{server.Address}:{server.Port}/files");
                string html = await ui.Content.ReadAsStringAsync();
                Check.That(ui.StatusCode).Is(HttpStatusCode.OK);
                Check.That(html).Contains("custom-client-v1");

                File.WriteAllText(indexPath, "<!doctype html><title>custom-client-v2</title>");
                HttpResponseMessage updatedUi = await client.GetAsync($"http://{server.Address}:{server.Port}/files/");
                string updatedHtml = await updatedUi.Content.ReadAsStringAsync();
                Check.That(updatedUi.StatusCode).Is(HttpStatusCode.OK);
                Check.That(updatedHtml).Contains("custom-client-v2");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Events_Should_Send_Connected_Event_When_Authorized() {
            string root = CreateRoot(nameof(Events_Should_Send_Connected_Event_When_Authorized));
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                using HttpRequestMessage request = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/api/events");
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);

                string firstEvent = await ReadSseUntilAsync(response, "filebrowser.connected");
                Check.That(firstEvent).Contains("event: filebrowser.connected");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Folder_Operation_Should_Emit_Completed_And_Changed_Events() {
            string root = CreateRoot(nameof(Folder_Operation_Should_Emit_Completed_And_Changed_Events));
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                using HttpRequestMessage eventsRequest = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/api/events");
                using HttpResponseMessage eventsResponse = await client.SendAsync(eventsRequest, HttpCompletionOption.ResponseHeadersRead);
                await using Stream eventsStream = await eventsResponse.Content.ReadAsStreamAsync();
                StringBuilder received = new();

                Check.That(eventsResponse.StatusCode).Is(HttpStatusCode.OK);
                await ReadSseUntilAsync(eventsStream, received, "filebrowser.connected");

                HttpResponseMessage create = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/folders",
                    JsonContent(new { path = "docs" })
                );

                Check.That(create.StatusCode).Is(HttpStatusCode.Accepted);

                string events = await ReadSseUntilAsync(eventsStream, received, "filebrowser.changed");
                Check.That(events).Contains("event: filebrowser.operation.completed");
                Check.That(events).Contains("event: filebrowser.changed");
                Check.That(events).Contains("createFolder");
                await WaitUntilAsync(() => Directory.Exists(Path.Combine(root, "docs")));
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Upload_Direct_Should_Emit_Progress_Completed_And_Changed_Events() {
            string root = CreateRoot(nameof(Upload_Direct_Should_Emit_Progress_Completed_And_Changed_Events));
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                using HttpRequestMessage eventsRequest = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/api/events");
                using HttpResponseMessage eventsResponse = await client.SendAsync(eventsRequest, HttpCompletionOption.ResponseHeadersRead);
                await using Stream eventsStream = await eventsResponse.Content.ReadAsStreamAsync();
                StringBuilder received = new();

                Check.That(eventsResponse.StatusCode).Is(HttpStatusCode.OK);
                await ReadSseUntilAsync(eventsStream, received, "filebrowser.connected");

                byte[] bytes = Encoding.UTF8.GetBytes("hello");
                Guid uploadId = await CreateUploadAsync(client, server, "docs/hello.txt", bytes.Length);

                using HttpRequestMessage uploadRequest = new(HttpMethod.Post, $"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}/files");
                uploadRequest.Headers.Add("X-File-Path", Uri.EscapeDataString("docs/hello.txt"));
                uploadRequest.Content = new ByteArrayContent(bytes);

                HttpResponseMessage upload = await client.SendAsync(uploadRequest);
                Check.That(upload.StatusCode).Is(HttpStatusCode.OK);

                string progressEvents = await ReadSseUntilAsync(eventsStream, received, "filebrowser.upload.progress");
                Check.That(progressEvents).Contains("event: filebrowser.upload.progress");

                HttpResponseMessage complete = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}/complete",
                    JsonContent(new { })
                );
                Check.That(complete.StatusCode).Is(HttpStatusCode.Accepted);

                string completeEvents = await ReadSseUntilAsync(eventsStream, received, "filebrowser.changed");
                Check.That(completeEvents).Contains("event: filebrowser.upload.completed");
                Check.That(completeEvents).Contains("event: filebrowser.changed");
                await WaitUntilAsync(() => File.Exists(Path.Combine(root, "docs", "hello.txt")));
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Upload_Direct_Should_Preserve_Directories_And_Replace_File() {
            string root = CreateRoot(nameof(Upload_Direct_Should_Preserve_Directories_And_Replace_File));
            var server = CreateAnonymousServer(root, 0, options => {
                options.UploadChunkThresholdBytes = 1024 * 1024;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();

                await UploadDirectAsync(client, server, "nested/file.txt", "v1");
                await WaitUntilAsync(() => File.Exists(Path.Combine(root, "nested", "file.txt")));
                Check.That(File.ReadAllText(Path.Combine(root, "nested", "file.txt"))).IsEqualTo("v1");

                await UploadDirectAsync(client, server, "nested/file.txt", "v2");
                await WaitUntilAsync(() => File.ReadAllText(Path.Combine(root, "nested", "file.txt")) == "v2");
                Check.That(File.ReadAllText(Path.Combine(root, "nested", "file.txt"))).IsEqualTo("v2");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Upload_Chunks_Should_Reassemble_File() {
            string root = CreateRoot(nameof(Upload_Chunks_Should_Reassemble_File));
            var server = CreateAnonymousServer(root, 0, options => {
                options.UploadChunkThresholdBytes = 4;
                options.UploadChunkBytes = 4;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                using HttpRequestMessage eventsRequest = new(HttpMethod.Get, $"http://{server.Address}:{server.Port}/files/api/events");
                using HttpResponseMessage eventsResponse = await client.SendAsync(eventsRequest, HttpCompletionOption.ResponseHeadersRead);
                await using Stream eventsStream = await eventsResponse.Content.ReadAsStreamAsync();
                StringBuilder received = new();

                Check.That(eventsResponse.StatusCode).Is(HttpStatusCode.OK);
                await ReadSseUntilAsync(eventsStream, received, "filebrowser.connected");

                byte[] payload = Encoding.UTF8.GetBytes("0123456789");

                Guid uploadId = await CreateUploadAsync(client, server, "big/file.bin", payload.Length);
                await SendChunkAsync(client, server, uploadId, "big/file.bin", payload.AsMemory(0, 4).ToArray(), 0);
                await SendChunkAsync(client, server, uploadId, "big/file.bin", payload.AsMemory(4, 4).ToArray(), 4);
                await SendChunkAsync(client, server, uploadId, "big/file.bin", payload.AsMemory(8, 2).ToArray(), 8);

                string progressEvents = await ReadSseUntilAsync(eventsStream, received, "filebrowser.upload.progress");
                Check.That(progressEvents).Contains("event: filebrowser.upload.progress");

                HttpResponseMessage complete = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}/complete",
                    JsonContent(new { })
                );

                Check.That(complete.StatusCode).Is(HttpStatusCode.Accepted);
                string completeEvents = await ReadSseUntilAsync(eventsStream, received, "filebrowser.changed");
                Check.That(completeEvents).Contains("event: filebrowser.upload.completed");
                Check.That(completeEvents).Contains("event: filebrowser.changed");
                await WaitUntilAsync(() => File.Exists(Path.Combine(root, "big", "file.bin")));
                Check.That(File.ReadAllText(Path.Combine(root, "big", "file.bin"))).IsEqualTo("0123456789");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Rename_Move_And_Delete_Should_Update_FileSystem() {
            string root = CreateRoot(nameof(Rename_Move_And_Delete_Should_Update_FileSystem));
            Directory.CreateDirectory(Path.Combine(root, "source", "child"));
            Directory.CreateDirectory(Path.Combine(root, "target"));
            File.WriteAllText(Path.Combine(root, "source", "file.txt"), "hello");

            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage rename = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/rename",
                    JsonContent(new { path = "source/file.txt", name = "renamed.txt" })
                );
                Check.That(rename.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => File.Exists(Path.Combine(root, "source", "renamed.txt")));
                Check.That(File.Exists(Path.Combine(root, "source", "renamed.txt"))).IsTrue();

                HttpResponseMessage move = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/move",
                    JsonContent(new { sourcePath = "source/renamed.txt", destinationDirectory = "target" })
                );
                Check.That(move.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => File.Exists(Path.Combine(root, "target", "renamed.txt")));
                Check.That(File.Exists(Path.Combine(root, "target", "renamed.txt"))).IsTrue();

                HttpResponseMessage moveIntoChild = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/move",
                    JsonContent(new { sourcePath = "source", destinationDirectory = "source/child" })
                );
                Check.That(moveIntoChild.StatusCode).Is(HttpStatusCode.Conflict);

                HttpResponseMessage delete = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/delete",
                    JsonContent(new { paths = new[] { "target/renamed.txt" } })
                );
                Check.That(delete.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => !File.Exists(Path.Combine(root, "target", "renamed.txt")));
                Check.That(File.Exists(Path.Combine(root, "target", "renamed.txt"))).IsFalse();
                Check.That(Directory.EnumerateFileSystemEntries(Path.Combine(root, ".trash")).Any()).IsTrue();
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Api_Should_Reject_Path_Traversal() {
            string root = CreateRoot(nameof(Api_Should_Reject_Path_Traversal));
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list?path=..%2Foutside");

                Check.That(response.StatusCode).Is(HttpStatusCode.BadRequest);
            }
            finally {
                await server.StopAsync();
            }
        }

        private static SimpleWServer CreateAnonymousServer(string root, int port, Action<FileBrowserOptions>? configure = null) {
            var server = new SimpleWServer(IPAddress.Loopback, port);
            server.UseFileBrowserModule(options => {
                options.Path = root;
                options.Prefix = "/files";
                options.AllowAnonymous = true;
                configure?.Invoke(options);
            });
            return server;
        }

        private static async Task UploadDirectAsync(HttpClient client, SimpleWServer server, string relativePath, string content) {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            Guid uploadId = await CreateUploadAsync(client, server, relativePath, bytes.Length);

            using HttpRequestMessage request = new(HttpMethod.Post, $"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}/files");
            request.Headers.Add("X-File-Path", Uri.EscapeDataString(relativePath));
            request.Content = new ByteArrayContent(bytes);

            HttpResponseMessage upload = await client.SendAsync(request);
            Check.That(upload.StatusCode).Is(HttpStatusCode.OK);

            HttpResponseMessage complete = await client.PostAsync(
                $"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}/complete",
                JsonContent(new { })
            );
            Check.That(complete.StatusCode).Is(HttpStatusCode.Accepted);
        }

        private static async Task<Guid> CreateUploadAsync(HttpClient client, SimpleWServer server, string relativePath, long size) {
            HttpResponseMessage response = await client.PostAsync(
                $"http://{server.Address}:{server.Port}/files/api/uploads",
                JsonContent(new { files = new[] { new { path = relativePath, size } } })
            );
            using JsonDocument json = await ReadJsonAsync(response);

            Check.That(response.StatusCode).Is(HttpStatusCode.Created);
            return json.RootElement.GetProperty("uploadId").GetGuid();
        }

        private static async Task SendChunkAsync(HttpClient client, SimpleWServer server, Guid uploadId, string relativePath, byte[] bytes, long offset) {
            using HttpRequestMessage request = new(HttpMethod.Post, $"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}/chunks");
            request.Headers.Add("X-File-Path", Uri.EscapeDataString(relativePath));
            request.Headers.Add("X-Chunk-Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Content = new ByteArrayContent(bytes);

            HttpResponseMessage response = await client.SendAsync(request);
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
        }

        private static StringContent JsonContent(object value) {
            return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        }

        private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) {
            string json = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json);
        }

        private static async Task WaitUntilAsync(Func<bool> predicate) {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            while (!predicate()) {
                await Task.Delay(25, timeout.Token);
            }
        }

        private static async Task<string> ReadSseUntilAsync(HttpResponseMessage response, string marker) {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            StringBuilder sb = new();
            return await ReadSseUntilAsync(stream, sb, marker, timeout.Token);
        }

        private static async Task<string> ReadSseUntilAsync(Stream stream, StringBuilder sb, string marker) {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
            return await ReadSseUntilAsync(stream, sb, marker, timeout.Token);
        }

        private static async Task<string> ReadSseUntilAsync(Stream stream, StringBuilder sb, string marker, CancellationToken cancellationToken) {
            byte[] buffer = new byte[4096];

            while (!cancellationToken.IsCancellationRequested) {
                int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read <= 0) {
                    break;
                }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                if (sb.ToString().Contains(marker, StringComparison.Ordinal)) {
                    return sb.ToString();
                }
            }

            return sb.ToString();
        }

        private static string CreateRoot(string testName) {
            string root = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
                testName + "_" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(root);
            return root;
        }

    }

}
