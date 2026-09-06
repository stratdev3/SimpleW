using System.IO.Compression;
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
        public void UseFileBrowserModule_Should_Validate_Page_Sizes() {
            string root = CreateRoot(nameof(UseFileBrowserModule_Should_Validate_Page_Sizes));
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            Check.ThatCode(() => {
                server.UseFileBrowserModule(options => {
                    options.Path = root;
                    options.AllowAnonymous = true;
                    options.DefaultPageSize = 101;
                    options.MaxPageSize = 100;
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
                Check.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{\"ok\":false,\"error\":\"forbidden\"}");

                HttpResponseMessage downloadResponse = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/download?path=private.txt");
                Check.That(downloadResponse.StatusCode).Is(HttpStatusCode.Forbidden);

                HttpResponseMessage trashResponse = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/trash");
                Check.That(trashResponse.StatusCode).Is(HttpStatusCode.Forbidden);

                HttpResponseMessage eventsResponse = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/events");
                Check.That(eventsResponse.StatusCode).Is(HttpStatusCode.Forbidden);
                Check.That(await eventsResponse.Content.ReadAsStringAsync()).IsEqualTo("Forbidden");

                HttpResponseMessage cancelResponse = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/operations/{Guid.NewGuid()}/cancel",
                    JsonContent(new { })
                );
                Check.That(cancelResponse.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Server_Challenge_Should_Handle_Ui_Api_And_Events() {
            string root = CreateRoot(nameof(Server_Challenge_Should_Handle_Ui_Api_And_Events));
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            HashSet<string> challengedPaths = new(StringComparer.Ordinal);

            server.ConfigureChallenge(session => {
                challengedPaths.Add(session.Request.Path);
                return session.Response.Redirect("/auth/login").SendAsync();
            });

            server.UseFileBrowserModule(options => {
                options.Path = root;
                options.Prefix = "/files";
                options.Authorize = _ => false;
            });

            await server.StartAsync();
            try {
                using HttpClientHandler handler = new() { AllowAutoRedirect = false };
                using HttpClient client = new(handler);
                string[] paths = [
                    "/files",
                    "/files/app.js",
                    "/files/api/config",
                    "/files/api/list",
                    "/files/api/events"
                ];

                foreach (string path in paths) {
                    using HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}{path}");
                    Check.That(response.StatusCode).Is(HttpStatusCode.Found);
                    Check.That(response.Headers.Location?.OriginalString).IsEqualTo("/auth/login");
                }

                Check.That(challengedPaths.Count).IsEqualTo(paths.Length);
                foreach (string path in paths) {
                    Check.That(challengedPaths.Contains(path)).IsTrue();
                }
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Ui_ClientPath_Should_Use_Server_Challenge_When_Authorize_Denies() {
            string root = CreateRoot(nameof(Ui_ClientPath_Should_Use_Server_Challenge_When_Authorize_Denies));
            string clientRoot = Path.Combine(root, "custom-client");
            Directory.CreateDirectory(clientRoot);
            File.WriteAllText(Path.Combine(clientRoot, "index.html"), "<!doctype html><title>custom-client</title>");
            File.WriteAllText(Path.Combine(clientRoot, "app.js"), "console.log('custom-client');");
            File.WriteAllText(Path.Combine(clientRoot, "styles.css"), "body{margin:0}");

            var server = new SimpleWServer(IPAddress.Loopback, 0);
            int challengeCount = 0;
            server.ConfigureChallenge(session => {
                challengeCount++;
                return session.Response.Status(401).Text("Authenticate").SendAsync();
            });
            server.UseFileBrowserModule(options => {
                options.Path = root;
                options.Prefix = "/files";
                options.ClientPath = clientRoot;
                options.Authorize = _ => false;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                using HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/app.js");

                Check.That(response.StatusCode).Is(HttpStatusCode.Unauthorized);
                Check.That(challengeCount).IsEqualTo(1);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Capability_And_Path_Denials_Should_Not_Invoke_Challenge() {
            string root = CreateRoot(nameof(Capability_And_Path_Denials_Should_Not_Invoke_Challenge));
            File.WriteAllText(Path.Combine(root, "private.txt"), "private");
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            int challengeCount = 0;

            server.ConfigureChallenge(session => {
                challengeCount++;
                return session.Response.Status(401).SendAsync();
            });

            server.UseFileBrowserModule(options => {
                options.Path = root;
                options.Prefix = "/files";
                options.ServeUi = false;
                options.EnableEvents = false;
                options.Authorize = _ => true;
                options.CanList = _ => true;
                options.CanDownload = _ => false;
                options.CanAccessPath = (_, _) => false;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();

                using HttpResponseMessage config = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/config");
                Check.That(config.StatusCode).Is(HttpStatusCode.OK);

                using HttpResponseMessage list = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list");
                Check.That(list.StatusCode).Is(HttpStatusCode.Forbidden);

                using HttpResponseMessage download = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/download?path=private.txt");
                Check.That(download.StatusCode).Is(HttpStatusCode.Forbidden);

                Check.That(challengeCount).IsEqualTo(0);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Granular_Capabilities_Should_Allow_ReadOnly_Path_Filtering() {
            string root = CreateRoot(nameof(Granular_Capabilities_Should_Allow_ReadOnly_Path_Filtering));
            Directory.CreateDirectory(Path.Combine(root, "public"));
            Directory.CreateDirectory(Path.Combine(root, "private"));
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            server.UseFileBrowserModule(options => {
                options.Path = root;
                options.Prefix = "/files";
                options.ServeUi = false;
                options.EnableEvents = false;
                options.CanList = _ => true;
                options.CanDownload = _ => false;
                options.CanAccessPath = (_, path) => path.Length == 0 || path.StartsWith("public", StringComparison.Ordinal);
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage list = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list");
                using JsonDocument json = await ReadJsonAsync(list);

                Check.That(list.StatusCode).Is(HttpStatusCode.OK);
                Check.That(json.RootElement.GetProperty("items").ToString()).Contains("public");
                Check.That(json.RootElement.GetProperty("items").ToString()).DoesNotContain("private");

                HttpResponseMessage download = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/download?path=public/file.txt");
                Check.That(download.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Operation_Status_Should_Be_Retained_And_Scope_Isolated() {
            string root = CreateRoot(nameof(Operation_Status_Should_Be_Retained_And_Scope_Isolated));
            var server = CreateAnonymousServer(root, 0, options => {
                options.EnableEvents = false;
                options.ScopeKey = session => session.Request.Headers.TryGetValue("X-Owner", out string? owner) ? owner! : string.Empty;
            });

            await server.StartAsync();
            try {
                using HttpClient owner = new();
                owner.DefaultRequestHeaders.Add("X-Owner", "owner-a");
                using HttpClient other = new();
                other.DefaultRequestHeaders.Add("X-Owner", "owner-b");

                HttpResponseMessage create = await owner.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/folders",
                    JsonContent(new { path = "owned" })
                );
                using JsonDocument accepted = await ReadJsonAsync(create);
                Guid operationId = accepted.RootElement.GetProperty("operationId").GetGuid();

                Check.That(create.StatusCode).Is(HttpStatusCode.Accepted);
                HttpResponseMessage foreign = await other.GetAsync($"http://{server.Address}:{server.Port}/files/api/operations/{operationId}");
                Check.That(foreign.StatusCode).Is(HttpStatusCode.NotFound);

                JsonDocument? status = null;
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
                while (!timeout.IsCancellationRequested) {
                    HttpResponseMessage response = await owner.GetAsync($"http://{server.Address}:{server.Port}/files/api/operations/{operationId}", timeout.Token);
                    status?.Dispose();
                    status = await ReadJsonAsync(response);
                    if (status.RootElement.GetProperty("isTerminal").GetBoolean()) {
                        break;
                    }
                    await Task.Delay(25, timeout.Token);
                }

                using (status) {
                    Check.That(status).IsNotNull();
                    Check.That(status!.RootElement.GetProperty("state").GetString()).IsEqualTo("completed");
                    Check.That(status.RootElement.GetProperty("payload").GetProperty("path").GetString()).IsEqualTo("owned");
                }
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
                Check.That(json.RootElement.GetProperty("search").GetString()).IsEqualTo("");
                Check.That(json.RootElement.GetProperty("sort").GetString()).IsEqualTo("name");
                Check.That(json.RootElement.GetProperty("direction").GetString()).IsEqualTo("asc");
                Check.That(json.RootElement.GetProperty("pageSize").GetInt32()).IsEqualTo(100);
                Check.That(json.RootElement.GetProperty("keyCount").GetInt32()).IsEqualTo(2);
                Check.That(json.RootElement.GetProperty("isTruncated").GetBoolean()).IsFalse();
                Check.That(json.RootElement.GetProperty("nextContinuationToken").ValueKind).IsEqualTo(JsonValueKind.Null);
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
        public async Task List_Should_Paginate_With_A_Stable_Continuation_Token() {
            string root = CreateRoot(nameof(List_Should_Paginate_With_A_Stable_Continuation_Token));
            for (int i = 0; i <= 100; i++) {
                string name = $"{i:D3}.txt";
                File.WriteAllText(Path.Combine(root, name), name);
            }

            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage firstResponse = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list");
                using JsonDocument first = await ReadJsonAsync(firstResponse);

                Check.That(firstResponse.StatusCode).Is(HttpStatusCode.OK);
                Check.That(first.RootElement.GetProperty("keyCount").GetInt32()).IsEqualTo(100);
                Check.That(first.RootElement.GetProperty("isTruncated").GetBoolean()).IsTrue();
                string token = first.RootElement.GetProperty("nextContinuationToken").GetString()!;
                string?[] firstNames = first.RootElement.GetProperty("items").EnumerateArray()
                    .Select(item => item.GetProperty("name").GetString())
                    .ToArray();
                Check.That(firstNames.First()).IsEqualTo("000.txt");
                Check.That(firstNames.Last()).IsEqualTo("099.txt");

                HttpResponseMessage secondResponse = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/list?continuationToken={Uri.EscapeDataString(token)}"
                );
                using JsonDocument second = await ReadJsonAsync(secondResponse);

                Check.That(secondResponse.StatusCode).Is(HttpStatusCode.OK);
                Check.That(second.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray())
                    .ContainsExactly("100.txt");
                Check.That(second.RootElement.GetProperty("isTruncated").GetBoolean()).IsFalse();
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task List_Should_Search_Only_Direct_Children_Case_Insensitively() {
            string root = CreateRoot(nameof(List_Should_Search_Only_Direct_Children_Case_Insensitively));
            Directory.CreateDirectory(Path.Combine(root, "archive"));
            File.WriteAllText(Path.Combine(root, "Annual-REPORT.pdf"), "report");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "notes");
            File.WriteAllText(Path.Combine(root, "archive", "nested-report.txt"), "nested");

            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/list?search=report"
                );
                using JsonDocument json = await ReadJsonAsync(response);

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(json.RootElement.GetProperty("keyCount").GetInt32()).IsEqualTo(1);
                Check.That(json.RootElement.GetProperty("items").EnumerateArray().Single().GetProperty("name").GetString()).IsEqualTo("Annual-REPORT.pdf");
                Check.That(json.RootElement.GetProperty("items").ToString()).DoesNotContain("nested-report.txt");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Theory]
        [InlineData("name", "asc", "a.txt,b.bin,c.log")]
        [InlineData("name", "desc", "c.log,b.bin,a.txt")]
        [InlineData("size", "asc", "a.txt,c.log,b.bin")]
        [InlineData("size", "desc", "b.bin,c.log,a.txt")]
        [InlineData("modified", "asc", "b.bin,c.log,a.txt")]
        [InlineData("modified", "desc", "a.txt,c.log,b.bin")]
        public async Task List_Should_Sort_By_The_Requested_Column(string sort, string direction, string expectedNames) {
            string root = CreateRoot($"{nameof(List_Should_Sort_By_The_Requested_Column)}_{sort}_{direction}");
            string a = Path.Combine(root, "a.txt");
            string b = Path.Combine(root, "b.bin");
            string c = Path.Combine(root, "c.log");
            File.WriteAllText(a, "1");
            File.WriteAllText(b, "333");
            File.WriteAllText(c, "22");
            File.SetLastWriteTimeUtc(a, new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(b, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(c, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));

            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/list?sort={sort}&direction={direction}"
                );
                using JsonDocument json = await ReadJsonAsync(response);

                string?[] names = json.RootElement.GetProperty("items").EnumerateArray()
                    .Select(item => item.GetProperty("name").GetString())
                    .ToArray();
                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(names).ContainsExactly(expectedNames.Split(','));
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task List_Should_Keep_Directories_Before_Files_For_Descending_Sorts() {
            string root = CreateRoot(nameof(List_Should_Keep_Directories_Before_Files_For_Descending_Sorts));
            Directory.CreateDirectory(Path.Combine(root, "folder"));
            File.WriteAllText(Path.Combine(root, "large.bin"), "large");

            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/list?sort=size&direction=desc"
                );
                using JsonDocument json = await ReadJsonAsync(response);

                string?[] types = json.RootElement.GetProperty("items").EnumerateArray()
                    .Select(item => item.GetProperty("type").GetString())
                    .ToArray();
                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(types).ContainsExactly("directory", "file");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task List_Should_Reject_Invalid_Paging_And_Cursor_Parameters() {
            string root = CreateRoot(nameof(List_Should_Reject_Invalid_Paging_And_Cursor_Parameters));
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");
            var server = CreateAnonymousServer(root, 0, options => {
                options.DefaultPageSize = 10;
                options.MaxPageSize = 10;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                Check.That((await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list?pageSize=11")).StatusCode)
                    .Is(HttpStatusCode.BadRequest);
                Check.That((await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list?sort=unknown")).StatusCode)
                    .Is(HttpStatusCode.BadRequest);
                Check.That((await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list?direction=sideways")).StatusCode)
                    .Is(HttpStatusCode.BadRequest);
                Check.That((await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/list?continuationToken=not-a-token")).StatusCode)
                    .Is(HttpStatusCode.BadRequest);

                HttpResponseMessage firstResponse = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/list?pageSize=1"
                );
                using JsonDocument first = await ReadJsonAsync(firstResponse);
                string token = first.RootElement.GetProperty("nextContinuationToken").GetString()!;
                HttpResponseMessage mismatched = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/list?pageSize=1&search=a&continuationToken={Uri.EscapeDataString(token)}"
                );
                Check.That(mismatched.StatusCode).Is(HttpStatusCode.BadRequest);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Download_Should_Return_The_Requested_File_As_An_Attachment() {
            string root = CreateRoot(nameof(Download_Should_Return_The_Requested_File_As_An_Attachment));
            File.WriteAllText(Path.Combine(root, "report.txt"), "download-content");
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/download?path=report.txt"
                );

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(response.Content.Headers.ContentDisposition?.DispositionType).IsEqualTo("attachment");
                Check.That(response.Content.Headers.ContentDisposition?.FileName).IsEqualTo("report.txt");
                Check.That(await response.Content.ReadAsStringAsync()).IsEqualTo("download-content");

                HttpResponseMessage traversal = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/download?path=..%2Foutside.txt"
                );
                Check.That(traversal.StatusCode).Is(HttpStatusCode.BadRequest);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Theory]
        [InlineData("literal%2Fname.txt")]
        [InlineData("%2e%2e")]
        public async Task Download_Query_Path_Should_Not_Be_Decoded_Twice(string fileName) {
            string root = CreateRoot(nameof(Download_Query_Path_Should_Not_Be_Decoded_Twice));
            File.WriteAllText(Path.Combine(root, fileName), "literal-percent-content");
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync(
                    $"http://{server.Address}:{server.Port}/files/api/download?path={Uri.EscapeDataString(fileName)}"
                );

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(await response.Content.ReadAsStringAsync()).IsEqualTo("literal-percent-content");
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
                Check.That(html).Contains("id=\"openTrash\"");
                Check.That(html).Contains("id=\"trashCount\" class=\"button-count\" hidden");
                Check.That(html).Contains("id=\"trashModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"trashList\" class=\"trash-list\"");
                Check.That(html).Contains("id=\"emptyTrash\" type=\"button\" disabled>Empty trash</button>");
                Check.That(html).Contains("id=\"purgeModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"restoreElsewhereModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"restoreElsewhereDestination\" type=\"text\"");
                Check.That(html).Contains("id=\"confirmRestoreElsewhere\" class=\"primary\" type=\"button\">Restore</button>");
                Check.That(html).Contains("id=\"newFolderModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"newFolderName\" type=\"text\"");
                Check.That(html).Contains("id=\"newFolderLocation\"");
                Check.That(html).Contains("id=\"confirmNewFolder\" class=\"primary\" type=\"button\">Create folder</button>");
                Check.That(html).Contains("class=\"icon-button modal-close\" type=\"button\" aria-label=\"Close\" title=\"Close\"");
                Check.That(html).Contains("class=\"close-glyph\" aria-hidden=\"true\"");
                Check.That(html).DoesNotContain("aria-label=\"Close\">Close</button>");
                Check.That(html).Contains("id=\"uploadModal\"");
                Check.That(html).Contains("id=\"uploadDrop\" class=\"drop\"");
                Check.That(html).Contains("id=\"chooseFiles\" class=\"drop-link\" type=\"button\">Select files</button>");
                Check.That(html).Contains("id=\"chooseFolder\" class=\"drop-link\" type=\"button\">Select folder</button>");
                Check.That(html).Contains("id=\"uploadStaging\" class=\"upload-staging\" aria-live=\"polite\" hidden");
                Check.That(html).Contains("id=\"startUpload\" class=\"primary\" type=\"button\" disabled>Upload</button>");
                Check.That(html).Contains("id=\"uploadDestination\">/</strong>");
                Check.That(html).Contains("id=\"renameModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"renameName\" type=\"text\"");
                Check.That(html).Contains("id=\"confirmRename\" class=\"primary\" type=\"button\">Rename</button>");
                Check.That(html).Contains("id=\"moveModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"moveDestination\" type=\"text\"");
                Check.That(html).Contains("id=\"confirmMove\" class=\"primary\" type=\"button\">Move</button>");
                Check.That(html).Contains("id=\"deleteModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"confirmDelete\" class=\"primary\" type=\"button\">Delete</button>");
                Check.That(html).Contains("id=\"archiveModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"archiveName\" type=\"text\"");
                Check.That(html).Contains("id=\"confirmArchive\" class=\"primary\" type=\"button\">Archive</button>");
                Check.That(html).Contains("id=\"extractModal\" class=\"modal\" hidden");
                Check.That(html).Contains("id=\"extractToFolder\" type=\"radio\" name=\"extractDestinationMode\" value=\"folder\" checked");
                Check.That(html).Contains("id=\"extractHere\" type=\"radio\" name=\"extractDestinationMode\" value=\"here\"");
                Check.That(html).Contains("id=\"confirmExtract\" class=\"primary\" type=\"button\">Extract</button>");
                Check.That(html).Contains("id=\"toggleOperations\"");
                Check.That(html).Contains("class=\"operations-glyph\"");
                Check.That(html).Contains("<circle cx=\"12\" cy=\"12\" r=\"3\"></circle>");
                Check.That(html).Contains("id=\"themeToggle\"");
                Check.That(html).Contains("class=\"browser-bar\"");
                Check.That(html).Contains("class=\"file-actions\"");
                Check.That(html).Contains("id=\"selectionBar\" class=\"selection-bar\" aria-live=\"polite\" aria-hidden=\"true\" inert");
                Check.That(html).Contains("id=\"selectionSummary\"");
                Check.That(html).Contains("id=\"selectionToggle\" class=\"header-selection\" type=\"checkbox\"");
                Check.That(html).Contains("id=\"downloadSelected\"");
                Check.That(html).Contains("id=\"archiveSelected\"");
                Check.That(html).DoesNotContain("id=\"selectionMenu\"");
                Check.That(html).DoesNotContain("id=\"invertSelection\"");
                Check.That(html).DoesNotContain(">Clear selection</button>");
                Check.That(html).Contains("id=\"delete\" type=\"button\"");
                Check.That(html).DoesNotContain("id=\"delete\" class=\"danger\"");
                Check.That(html).Contains("id=\"operationCount\" class=\"badge\" aria-hidden=\"true\" hidden>0/0</span>");
                Check.That(html).Contains("id=\"operationsPanel\" class=\"operations-panel\" aria-hidden=\"true\"");
                Check.That(html).Contains("id=\"clearOperations\"");
                Check.That(html).Contains("id=\"cancelOperations\"");
                Check.That(html).Contains("id=\"globalProgress\" class=\"global-progress\" value=\"0\" max=\"100\" hidden");
                Check.That(html).Contains("id=\"search\"");
                Check.That(html).Contains("class=\"clear-search-glyph\"");
                Check.That(html).Contains("title=\"Clear search\" hidden");
                Check.That(html).Contains("id=\"pageSize\"");
                Check.That(html).DoesNotContain("id=\"displayDensity\"");
                Check.That(html).DoesNotContain("value=\"compact\"");
                Check.That(html).Contains("id=\"previousPage\"");
                Check.That(html).Contains("id=\"nextPage\"");
                Check.That(html).Contains("data-sort=\"modified\"");
                Check.That(html).Contains("<h2>Operations</h2>");
                Check.That(html).DoesNotContain("id=\"drop\"");
                Check.That(html).DoesNotContain("id=\"queue\"");

                HttpResponseMessage css = await client.GetAsync($"http://{server.Address}:{server.Port}/files/styles.css");
                string cssText = await css.Content.ReadAsStringAsync();
                Check.That(css.StatusCode).Is(HttpStatusCode.OK);
                Check.That(css.Content.Headers.ContentType?.MediaType).IsEqualTo("text/css");
                Check.That(cssText).Contains(".shell");
                Check.That(cssText).Contains(".folder-icon");
                Check.That(cssText).Contains(".file-icon");
                Check.That(cssText).Contains(".clear-search-button[hidden]");
                Check.That(cssText).Contains(".upload-staging[hidden]");
                Check.That(cssText).Contains(".upload-staged-item");
                Check.That(cssText).Contains(".trash-modal-panel");
                Check.That(cssText).Contains(".trash-item-actions");
                Check.That(cssText).Contains(".trash-list-message");
                Check.That(cssText).Contains(".drop-link");
                Check.That(cssText).Contains(".pane.upload-drop-current");
                Check.That(cssText).Contains("tbody tr.directory-row.upload-drop-target");
                Check.That(cssText).Contains(".upload-destination");
                Check.That(cssText).Contains(".modal-form-field");
                Check.That(cssText).Contains(".modal-message");
                Check.That(cssText).Contains(".archive-name-field");
                Check.That(cssText).Contains(".extract-options");
                Check.That(cssText).Contains(".extract-safety-note");
                Check.That(cssText).Contains("tbody tr:not(.list-message):hover");
                Check.That(cssText).Contains("tbody tr.directory-row { cursor: pointer; }");
                Check.That(cssText).Contains(".item-row-actions");
                Check.That(cssText).Contains("tbody tr:not(.list-message):hover .item-row-actions");
                Check.That(cssText).Contains("tbody tr:hover .row-selection");
                Check.That(cssText).Contains("tbody tr.selected .row-selection");
                Check.That(cssText).Contains(":root[data-theme=\"dark\"]");
                Check.That(cssText).Contains("td:first-child { width: 100%; }");
                Check.That(cssText).Contains(".file-link:hover");
                Check.That(cssText).Contains(".file-actions");
                Check.That(cssText).Contains(".browser-bar.selection-active > .selection-bar");
                Check.That(cssText).Contains(".browser-bar.selection-active > .file-actions");
                Check.That(cssText).Contains(".header-selection");
                Check.That(cssText).DoesNotContain(".selection-menu");
                Check.That(cssText).Contains("flex-wrap: nowrap;");
                Check.That(cssText).DoesNotContain(":root[data-density=\"compact\"]");
                Check.That(cssText).Contains("position: sticky;");
                Check.That(cssText).Contains("top: var(--browser-bar-height, 53px);");
                Check.That(cssText).Contains("overflow: visible;");
                Check.That(cssText).DoesNotContain("scrollbar-gutter: stable;");
                Check.That(cssText).DoesNotContain("max-height: max(240px, calc(100vh");
                Check.That(cssText).Contains("main.operations-open .operations-panel");
                Check.That(cssText).Contains(".operations-panel progress[hidden]");
                Check.That(cssText).Contains(".operation-status-badge");
                Check.That(cssText).Contains(".operation-progress-row");
                Check.That(cssText).Contains(".operation-item.operation-state-running");
                Check.That(cssText).Contains("button:not(:disabled):hover");
                Check.That(cssText).Contains("button.primary:not(:disabled):hover");
                Check.That(cssText).Contains(".modal-close:not(:disabled):hover");
                Check.That(cssText).Contains(".close-glyph::before");

                HttpResponseMessage js = await client.GetAsync($"http://{server.Address}:{server.Port}/files/app.js");
                string jsText = await js.Content.ReadAsStringAsync();
                Check.That(js.StatusCode).Is(HttpStatusCode.OK);
                Check.That(js.Content.Headers.ContentType?.MediaType).IsEqualTo("text/javascript");
                Check.That(jsText).Contains("loadConfig");
                Check.That(jsText).Contains("loadTrash");
                Check.That(jsText).Contains("restoreTrashItems");
                Check.That(jsText).Contains("openRestoreElsewhereModal");
                Check.That(jsText).Contains("performRestoreElsewhere");
                Check.That(jsText).Contains("canRestoreElsewhere");
                Check.That(jsText).Contains("openPurgeModal");
                Check.That(jsText).Contains("`${api}/trash/restore`");
                Check.That(jsText).Contains("`${api}/trash/delete`");
                Check.That(jsText).Contains("`${api}/trash/empty`");
                Check.That(jsText).Contains("uploadModal");
                Check.That(jsText).Contains("createStagedUploadItems");
                Check.That(jsText).Contains("addStagedUploadItems");
                Check.That(jsText).Contains("removeStagedUploadItem");
                Check.That(jsText).Contains("upload-remove-glyph");
                Check.That(jsText).Contains("collectDroppedUploadItems");
                Check.That(jsText).Contains("startUploadButton.onclick = uploadStagedItems");
                Check.That(jsText).Contains("updateBrowserUploadDropTarget");
                Check.That(jsText).Contains("browserPane.addEventListener(\"drop\"");
                Check.That(jsText).Contains("openUploadModal(destinationPath)");
                Check.That(jsText).Contains("startUpload(files, destinationPath)");
                Check.That(jsText).Contains("trackQueuedOperation");
                Check.That(jsText).Contains("cancelAllOperations");
                Check.That(jsText).Contains("clearOperationHistory");
                Check.That(jsText).Contains("runningCount");
                Check.That(jsText).Contains("queuedCount");
                Check.That(jsText).Contains("globalProgress.hidden = true");
                Check.That(jsText).Contains("globalProgress.hidden = false");
                Check.That(jsText).Contains("operationStateLabel");
                Check.That(jsText).Contains("operation-progress-label");
                Check.That(jsText).Contains("scheduleOperationRender");
                Check.That(jsText).DoesNotContain("status: \"server received\"");
                Check.That(jsText).DoesNotContain("status: \"uploading\"");
                Check.That(jsText).Contains("filebrowser.operation.cancelled");
                Check.That(jsText).Contains("filebrowser.upload.cancelled");
                Check.That(jsText).Contains("continuationToken");
                Check.That(jsText).Contains("syncNavigationState");
                Check.That(jsText).Contains("simplew.filebrowser.theme");
                Check.That(jsText).Contains("applyTheme");
                Check.That(jsText).DoesNotContain("simplew.filebrowser.density");
                Check.That(jsText).DoesNotContain("applyDensity");
                Check.That(jsText).Contains("syncStickyTableHeader");
                Check.That(jsText).Contains("ResizeObserver");
                Check.That(jsText).Contains("/download?");
                Check.That(jsText).Contains("setTimeout(applySearch, 300)");
                Check.That(jsText).Contains("class=\"row-selection\" type=\"checkbox\"");
                Check.That(jsText).Contains("setRowSelection");
                Check.That(jsText).Contains("checkbox.onclick = e => e.stopPropagation()");
                Check.That(jsText).Contains("tr.classList.add(\"directory-row\")");
                Check.That(jsText).Contains("data-item-action=\"rename\"");
                Check.That(jsText).Contains("data-item-action=\"move\"");
                Check.That(jsText).Contains("data-item-action=\"delete\"");
                Check.That(jsText).Contains("renameItem(item.path)");
                Check.That(jsText).Contains("moveItems([item.path])");
                Check.That(jsText).Contains("deleteItems([item.path])");
                Check.That(jsText).Contains("openRenameModal(path)");
                Check.That(jsText).Contains("openMoveModal(paths)");
                Check.That(jsText).Contains("openDeleteModal(paths)");
                Check.That(jsText).Contains("runAction(performRename)");
                Check.That(jsText).Contains("runAction(performMove)");
                Check.That(jsText).Contains("runAction(performDelete)");
                Check.That(jsText).Contains("openNewFolderModal");
                Check.That(jsText).Contains("runAction(createNewFolder)");
                Check.That(jsText).DoesNotContain("prompt(\"Folder name\"");
                Check.That(jsText).DoesNotContain("prompt(\"New name\"");
                Check.That(jsText).DoesNotContain("prompt(\"Destination folder\"");
                Check.That(jsText).DoesNotContain("confirm(`Delete");
                Check.That(jsText).Contains("data-item-action=\"archive\"");
                Check.That(jsText).Contains("openArchiveModal([item.path])");
                Check.That(jsText).Contains("archiveSelectedButton.onclick = () => openArchiveModal([...selected])");
                Check.That(jsText).Contains("`${api}/archive`");
                Check.That(jsText).Contains("data-item-action=\"extract\"");
                Check.That(jsText).Contains("item.name.toLowerCase().endsWith(\".zip\")");
                Check.That(jsText).Contains("openExtractModal(item.path)");
                Check.That(jsText).Contains("`${api}/extract`");
                Check.That(jsText).Contains("tr.onclick = () => navigateTo(item.path)");
                Check.That(jsText).DoesNotContain("setRowSelection(tr, checkbox, item.path, !selected.has(item.path))");
                Check.That(jsText).Contains("downloadSelectedFiles");
                Check.That(jsText).Contains("clearSelection");
                Check.That(jsText).Contains("selectionToggle.indeterminate = partlySelected");
                Check.That(jsText).Contains("selectionToggle.onchange = () =>");
                Check.That(jsText).DoesNotContain("setSelectionMenuOpen");
                Check.That(jsText).Contains("updateSortIndicators");
                Check.That(jsText).Contains("browserBar.classList.toggle(\"selection-active\", hasSelection)");
                Check.That(jsText).Contains("selectionBar.removeAttribute(\"inert\")");
                Check.That(jsText).Contains("selectAllCurrentItems");
                Check.That(jsText).Contains("activateSelectedItem");
                Check.That(jsText).Contains("e.key === \"Delete\"");
                Check.That(jsText).Contains("e.key === \"F2\"");
                Check.That(jsText).DoesNotContain("if (!e.ctrlKey && !e.metaKey) selected.clear()");
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
                Check.That(configJson.RootElement.GetProperty("defaultPageSize").GetInt32()).IsEqualTo(100);
                Check.That(configJson.RootElement.GetProperty("maxPageSize").GetInt32()).IsEqualTo(1000);

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
        public async Task Upload_Header_Path_Should_Be_Decoded_Once() {
            string root = CreateRoot(nameof(Upload_Header_Path_Should_Be_Decoded_Once));
            var server = CreateAnonymousServer(root, 0);

            await server.StartAsync();
            try {
                using HttpClient client = new();
                const string relativePath = "encoded directory/literal%2Fname.txt";

                await UploadDirectAsync(client, server, relativePath, "literal-percent-content");

                string filePath = Path.Combine(root, "encoded directory", "literal%2Fname.txt");
                await WaitUntilAsync(() => File.Exists(filePath));
                Check.That(File.ReadAllText(filePath)).IsEqualTo("literal-percent-content");
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
        public async Task Upload_Status_Should_Return_Received_Ranges_For_Resume() {
            string root = CreateRoot(nameof(Upload_Status_Should_Return_Received_Ranges_For_Resume));
            var server = CreateAnonymousServer(root, 0, options => {
                options.UploadChunkThresholdBytes = 4;
                options.UploadChunkBytes = 4;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                Guid uploadId = await CreateUploadAsync(client, server, "resume/file.bin", 8);
                await SendChunkAsync(client, server, uploadId, "resume/file.bin", Encoding.UTF8.GetBytes("4567"), 4);

                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}");
                using JsonDocument json = await ReadJsonAsync(response);

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(json.RootElement.GetProperty("uploadId").GetGuid()).IsEqualTo(uploadId);
                Check.That(json.RootElement.GetProperty("receivedBytes").GetInt64()).IsEqualTo(4);
                JsonElement file = json.RootElement.GetProperty("files")[0];
                Check.That(file.GetProperty("path").GetString()).IsEqualTo("resume/file.bin");
                Check.That(file.GetProperty("completed").GetBoolean()).IsFalse();
                JsonElement range = file.GetProperty("receivedRanges")[0];
                Check.That(range.GetProperty("start").GetInt64()).IsEqualTo(4);
                Check.That(range.GetProperty("end").GetInt64()).IsEqualTo(8);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Upload_Delete_Should_Cancel_Session_And_Remove_Parts() {
            string root = CreateRoot(nameof(Upload_Delete_Should_Cancel_Session_And_Remove_Parts));
            var server = CreateAnonymousServer(root, 0, options => {
                options.UploadChunkThresholdBytes = 4;
                options.UploadChunkBytes = 4;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                Guid uploadId = await CreateUploadAsync(client, server, "cancel/file.bin", 8);
                await SendChunkAsync(client, server, uploadId, "cancel/file.bin", Encoding.UTF8.GetBytes("0123"), 0);
                string tempPath = Path.Combine(root, ".filebrowser-tmp");
                Check.That(Directory.EnumerateFiles(tempPath, "*.part").Count()).IsEqualTo(1);

                HttpResponseMessage delete = await client.DeleteAsync($"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}");
                Check.That(delete.StatusCode).Is(HttpStatusCode.OK);
                Check.That(Directory.EnumerateFiles(tempPath, "*.part").Count()).IsEqualTo(0);

                HttpResponseMessage status = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}");
                Check.That(status.StatusCode).Is(HttpStatusCode.NotFound);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Upload_Create_Should_Enforce_Maximum_Concurrent_Sessions() {
            string root = CreateRoot(nameof(Upload_Create_Should_Enforce_Maximum_Concurrent_Sessions));
            var server = CreateAnonymousServer(root, 0, options => {
                options.MaxConcurrentUploadSessions = 1;
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                Guid uploadId = await CreateUploadAsync(client, server, "first.bin", 1);
                HttpResponseMessage rejected = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/uploads",
                    JsonContent(new { files = new[] { new { path = "second.bin", size = 1 } } })
                );
                Check.That(rejected.StatusCode).Is(HttpStatusCode.TooManyRequests);

                HttpResponseMessage delete = await client.DeleteAsync($"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}");
                Check.That(delete.StatusCode).Is(HttpStatusCode.OK);
                await CreateUploadAsync(client, server, "third.bin", 1);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Upload_Session_Should_Expire_And_Remove_Parts() {
            string root = CreateRoot(nameof(Upload_Session_Should_Expire_And_Remove_Parts));
            var server = CreateAnonymousServer(root, 0, options => {
                options.UploadChunkThresholdBytes = 4;
                options.UploadChunkBytes = 4;
                options.UploadSessionTimeout = TimeSpan.FromMilliseconds(100);
            });

            await server.StartAsync();
            try {
                using HttpClient client = new();
                Guid uploadId = await CreateUploadAsync(client, server, "expired/file.bin", 8);
                await SendChunkAsync(client, server, uploadId, "expired/file.bin", Encoding.UTF8.GetBytes("0123"), 0);
                string tempPath = Path.Combine(root, ".filebrowser-tmp");

                await WaitUntilAsync(() => !Directory.EnumerateFiles(tempPath, "*.part").Any());
                HttpResponseMessage status = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/uploads/{uploadId}");
                Check.That(status.StatusCode).Is(HttpStatusCode.NotFound);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public void Upload_Install_Should_Remove_Old_Abandoned_Parts() {
            string root = CreateRoot(nameof(Upload_Install_Should_Remove_Old_Abandoned_Parts));
            string tempPath = Path.Combine(root, ".filebrowser-tmp");
            Directory.CreateDirectory(tempPath);
            string abandonedPart = Path.Combine(tempPath, "abandoned.part");
            File.WriteAllText(abandonedPart, "partial");
            File.SetLastWriteTimeUtc(abandonedPart, DateTime.UtcNow.AddHours(-1));

            CreateAnonymousServer(root, 0, options => {
                options.UploadSessionTimeout = TimeSpan.FromMinutes(30);
            });

            Check.That(File.Exists(abandonedPart)).IsFalse();
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
        public async Task Trash_Should_List_Restore_Delete_Permanently_And_Empty() {
            string root = CreateRoot(nameof(Trash_Should_List_Restore_Delete_Permanently_And_Empty));
            File.WriteAllText(Path.Combine(root, "restore.txt"), "restore me");
            File.WriteAllText(Path.Combine(root, "purge.txt"), "purge me");
            Directory.CreateDirectory(Path.Combine(root, ".trash"));
            const string legacyId = "20260905010101001-legacy_file.txt";
            File.WriteAllText(Path.Combine(root, ".trash", legacyId), "legacy file");

            var server = CreateAnonymousServer(root, 0);
            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage delete = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/delete",
                    JsonContent(new { paths = new[] { "restore.txt", "purge.txt" } })
                );
                Check.That(delete.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => !File.Exists(Path.Combine(root, "restore.txt")) && !File.Exists(Path.Combine(root, "purge.txt")));

                HttpResponseMessage list = await client.GetAsync($"http://{server.Address}:{server.Port}/files/api/trash");
                using JsonDocument listJson = await ReadJsonAsync(list);
                Check.That(list.StatusCode).Is(HttpStatusCode.OK);
                JsonElement[] items = listJson.RootElement.GetProperty("items").EnumerateArray().ToArray();
                Check.That(items.Length).IsEqualTo(3);
                JsonElement restoreItem = items.Single(item => item.GetProperty("originalPath").GetString() == "restore.txt");
                JsonElement purgeItem = items.Single(item => item.GetProperty("originalPath").GetString() == "purge.txt");
                JsonElement legacyItem = items.Single(item => item.GetProperty("id").GetString() == legacyId);
                Check.That(restoreItem.GetProperty("canRestore").GetBoolean()).IsTrue();
                Check.That(legacyItem.GetProperty("canRestore").GetBoolean()).IsFalse();
                Check.That(legacyItem.GetProperty("canRestoreElsewhere").GetBoolean()).IsTrue();

                string restoreId = restoreItem.GetProperty("id").GetString()!;
                HttpResponseMessage restore = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/trash/restore",
                    JsonContent(new { ids = new[] { restoreId } })
                );
                Check.That(restore.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => File.Exists(Path.Combine(root, "restore.txt")));
                Check.That(File.ReadAllText(Path.Combine(root, "restore.txt"))).IsEqualTo("restore me");

                string purgeId = purgeItem.GetProperty("id").GetString()!;
                HttpResponseMessage purge = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/trash/delete",
                    JsonContent(new { ids = new[] { purgeId } })
                );
                Check.That(purge.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => !Directory.Exists(Path.Combine(root, ".trash", purgeId)));

                HttpResponseMessage restoreLegacy = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/trash/restore",
                    JsonContent(new { ids = new[] { legacyId }, destinationPath = "recovered/legacy.txt" })
                );
                Check.That(restoreLegacy.StatusCode).Is(HttpStatusCode.Accepted);
                string restoredLegacyPath = Path.Combine(root, "recovered", "legacy.txt");
                await WaitUntilAsync(() => File.Exists(restoredLegacyPath));
                Check.That(File.ReadAllText(restoredLegacyPath)).IsEqualTo("legacy file");

                File.WriteAllText(Path.Combine(root, "empty.txt"), "empty me");
                HttpResponseMessage deleteForEmpty = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/delete",
                    JsonContent(new { paths = new[] { "empty.txt" } })
                );
                Check.That(deleteForEmpty.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => !File.Exists(Path.Combine(root, "empty.txt")));

                HttpResponseMessage empty = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/trash/empty",
                    JsonContent(new { })
                );
                Check.That(empty.StatusCode).Is(HttpStatusCode.Accepted);
                await WaitUntilAsync(() => !Directory.EnumerateFileSystemEntries(Path.Combine(root, ".trash")).Any());
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Archive_Should_Include_Selected_File_And_Complete_Directory() {
            string root = CreateRoot(nameof(Archive_Should_Include_Selected_File_And_Complete_Directory));
            Directory.CreateDirectory(Path.Combine(root, "folder", "empty"));
            File.WriteAllText(Path.Combine(root, "file.txt"), "root file");
            File.WriteAllText(Path.Combine(root, "folder", "nested.txt"), "nested file");

            var server = CreateAnonymousServer(root, 0);
            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage response = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/archive",
                    JsonContent(new {
                        paths = new[] { "file.txt", "folder" },
                        destinationPath = "bundle.zip"
                    })
                );

                Check.That(response.StatusCode).Is(HttpStatusCode.Accepted);
                string archivePath = Path.Combine(root, "bundle.zip");
                await WaitUntilAsync(() => File.Exists(archivePath));

                using ZipArchive archive = ZipFile.OpenRead(archivePath);
                string[] entries = archive.Entries.Select(entry => entry.FullName).ToArray();
                Check.That(entries).Contains("file.txt");
                Check.That(entries).Contains("folder/");
                Check.That(entries).Contains("folder/nested.txt");
                Check.That(entries).Contains("folder/empty/");

                ZipArchiveEntry nestedEntry = archive.GetEntry("folder/nested.txt")!;
                using StreamReader reader = new(nestedEntry.Open());
                Check.That(reader.ReadToEnd()).IsEqualTo("nested file");
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Extract_Should_Extract_Zip_Here_Or_Into_A_New_Directory() {
            string root = CreateRoot(nameof(Extract_Should_Extract_Zip_Here_Or_Into_A_New_Directory));
            string dedicatedArchivePath = Path.Combine(root, "dedicated.zip");
            using (ZipArchive archive = ZipFile.Open(dedicatedArchivePath, ZipArchiveMode.Create)) {
                ZipArchiveEntry entry = archive.CreateEntry("nested/file.txt");
                using StreamWriter writer = new(entry.Open());
                writer.Write("dedicated");
            }

            string hereArchivePath = Path.Combine(root, "here.zip");
            using (ZipArchive archive = ZipFile.Open(hereArchivePath, ZipArchiveMode.Create)) {
                ZipArchiveEntry entry = archive.CreateEntry("here.txt");
                using StreamWriter writer = new(entry.Open());
                writer.Write("here");
            }

            var server = CreateAnonymousServer(root, 0);
            await server.StartAsync();
            try {
                using HttpClient client = new();

                HttpResponseMessage dedicated = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/extract",
                    JsonContent(new {
                        path = "dedicated.zip",
                        destinationDirectory = "dedicated",
                        createDestinationDirectory = true
                    })
                );
                Check.That(dedicated.StatusCode).Is(HttpStatusCode.Accepted);
                string dedicatedFile = Path.Combine(root, "dedicated", "nested", "file.txt");
                await WaitUntilAsync(() => File.Exists(dedicatedFile));
                Check.That(File.ReadAllText(dedicatedFile)).IsEqualTo("dedicated");

                HttpResponseMessage here = await client.PostAsync(
                    $"http://{server.Address}:{server.Port}/files/api/extract",
                    JsonContent(new {
                        path = "here.zip",
                        destinationDirectory = "",
                        createDestinationDirectory = false
                    })
                );
                Check.That(here.StatusCode).Is(HttpStatusCode.Accepted);
                string hereFile = Path.Combine(root, "here.txt");
                await WaitUntilAsync(() => File.Exists(hereFile));
                Check.That(File.ReadAllText(hereFile)).IsEqualTo("here");
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
