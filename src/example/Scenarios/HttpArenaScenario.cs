using System.Buffers;
using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleW;
using SimpleW.Modules;


namespace example;

internal sealed class HttpArenaScenario : IScenario {

    private const string RepositoryTreeUrl = "https://api.github.com/repos/MDA2AV/HttpArena/git/trees/main?recursive=1";

    private const string RepositoryRawBaseUrl = "https://raw.githubusercontent.com/MDA2AV/HttpArena/main/";

    private const string StaticPathPrefix = "data/static/";

    public string Name => "httparena";

    public string Description => @"API define by https://www.http-arena.com.";

    public string BrowserPath => "/api/test/hello";

    public string Usage => " [--static-directory path]";

    public async Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("static-directory");

        string? configuredStaticDirectory = arguments.Get("static-directory");
        string staticDirectory = configuredStaticDirectory == null
                                   ? Path.Combine(AppContext.BaseDirectory, "http-arena-data-static")
                                   : Path.GetFullPath(configuredStaticDirectory);
        await EnsureStaticAssetsAsync(staticDirectory, cancellationToken).ConfigureAwait(false);

        SimpleWServer server = ExampleServer.Create(arguments);

        server.MapGet("/baseline11", (int a, int b, HttpSession s) => Text(s, a + b));
        server.MapPost("/baseline11", (int a, int b, HttpSession s) => Text(s, a + b + ParseInt(s.Request.Body)));

        server.MapGet("/pipeline", (HttpSession s) => s.Response.Text("ok"));

        server.MapPost("/upload", (HttpSession s) => Text(s, s.Request.Body.Length));

        server.MapGet("/api/test/hello", () => new { message = "Hello World !" });

        server.UseWebSocketModule(ws => {
            ws.OnBinary((conn, ctx, msg) => conn.SendBinaryAsync(msg));
            ws.OnUnknown((conn, ctx, msg) => conn.SendTextAsync(msg.RawText));
        });

        server.UseStaticFilesModule(options => {
            options.Path = staticDirectory;
            options.Prefix = "/static/";
            options.AutoIndex = false;
            options.CompressedDiskCache = true;
        });

        await ExampleServer.RunAsync(server, "http", arguments, cancellationToken).ConfigureAwait(false);
    }

    #region static dir

    private static async Task EnsureStaticAssetsAsync(string staticDirectory, CancellationToken cancellationToken) {
        if (File.Exists(staticDirectory)) {
            throw new IOException($"HttpArena static path '{staticDirectory}' is a file; a directory is required.");
        }
        if (Directory.Exists(staticDirectory) && Directory.EnumerateFileSystemEntries(staticDirectory).Any()) {
            ExampleConsole.Success($"HttpArena static assets already exist in '{staticDirectory}'.");
            return;
        }

        string? parentDirectory = Path.GetDirectoryName(staticDirectory);
        if (string.IsNullOrWhiteSpace(parentDirectory)) {
            throw new InvalidOperationException($"Unable to resolve the parent directory of '{staticDirectory}'.");
        }

        Directory.CreateDirectory(parentDirectory);
        string directoryName = Path.GetFileName(staticDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string stagingDirectory = Path.Combine(parentDirectory, $".{directoryName}.httparena-{Guid.NewGuid():N}.tmp");

        ExampleConsole.Info($"HttpArena static directory is empty: '{staticDirectory}'.");
        ExampleConsole.Info($"Reading assets from {RepositoryTreeUrl}");

        bool published = false;
        try {
            Directory.CreateDirectory(stagingDirectory);

            using HttpClient client = CreateHttpClient();
            GitTreeResponse tree = await GetRepositoryTreeAsync(client, cancellationToken).ConfigureAwait(false);
            if (tree.Truncated) {
                throw new InvalidOperationException("GitHub returned a truncated repository tree; the HttpArena asset set would be incomplete.");
            }

            List<GitTreeEntry> entries = tree.Tree
                ?? throw new InvalidOperationException("GitHub repository tree response does not contain a tree.");
            List<GitTreeEntry> files = entries
                                                .Where(static entry => string.Equals(entry.Type, "blob", StringComparison.Ordinal)
                                                                    && entry.Path.StartsWith(StaticPathPrefix, StringComparison.Ordinal))
                                                .OrderBy(static entry => entry.Path, StringComparer.Ordinal)
                                                .ToList();

            if (files.Count == 0) {
                throw new InvalidOperationException($"GitHub returned no files below '{StaticPathPrefix}'.");
            }
            if (files.Any(static file => file.Size < 0)) {
                throw new InvalidOperationException("GitHub returned an invalid size for an HttpArena static asset.");
            }

            ExampleConsole.Info($"Downloading {files.Count} HttpArena static assets...");
            DateTime commonTimestampUtc = DateTime.UtcNow;
            long downloadedBytes = 0;

            for (int index = 0; index < files.Count; index++) {
                cancellationToken.ThrowIfCancellationRequested();

                GitTreeEntry file = files[index];
                string relativePath = file.Path[StaticPathPrefix.Length..];
                string destinationPath = ResolveDestinationPath(stagingDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                ExampleConsole.Progress(index + 1, files.Count, relativePath, file.Size);
                long length = await DownloadFileAsync(client, file, destinationPath, cancellationToken).ConfigureAwait(false);
                downloadedBytes += length;
                File.SetLastWriteTimeUtc(destinationPath, commonTimestampUtc);
            }

            PublishStagingDirectory(stagingDirectory, staticDirectory);
            published = true;

            ExampleConsole.Success($"Downloaded {files.Count} HttpArena assets ({downloadedBytes} bytes) to '{staticDirectory}'.");
        }
        finally {
            if (!published && Directory.Exists(stagingDirectory)) {
                try {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
                catch (Exception ex) {
                    ExampleConsole.Warning(
                        $"Unable to remove incomplete HttpArena download '{stagingDirectory}': {ex.Message}",
                        writeToError: true
                    );
                }
            }
        }
    }

    private static HttpClient CreateHttpClient() {
        HttpClient client = new();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimpleW-HttpArena-example", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static async Task<GitTreeResponse> GetRepositoryTreeAsync(HttpClient client, CancellationToken cancellationToken) {
        using HttpResponseMessage response = await client.GetAsync(
                                                 RepositoryTreeUrl,
                                                 HttpCompletionOption.ResponseHeadersRead,
                                                 cancellationToken
                                             ).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        GitTreeResponse? tree = await JsonSerializer.DeserializeAsync<GitTreeResponse>(
                                    content,
                                    cancellationToken: cancellationToken
                                ).ConfigureAwait(false);

        return tree ?? throw new InvalidOperationException("GitHub returned an empty repository tree response.");
    }

    private static async Task<long> DownloadFileAsync(HttpClient client, GitTreeEntry file, string destinationPath, CancellationToken cancellationToken) {
        string escapedPath = string.Join("/", file.Path.Split('/').Select(Uri.EscapeDataString));
        using HttpResponseMessage response = await client.GetAsync(
                                                 RepositoryRawBaseUrl + escapedPath,
                                                 HttpCompletionOption.ResponseHeadersRead,
                                                 cancellationToken
                                             ).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using FileStream output = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        await response.Content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);

        long downloadedLength = output.Length;
        if (downloadedLength != file.Size) {
            throw new InvalidOperationException(
                $"Downloaded size mismatch for '{file.Path}': expected {file.Size} bytes, received {downloadedLength} bytes."
            );
        }
        return downloadedLength;
    }

    private static string ResolveDestinationPath(string stagingDirectory, string relativePath) {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) {
            throw new InvalidOperationException($"GitHub returned an invalid asset path '{relativePath}'.");
        }

        string root = Path.GetFullPath(stagingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string destination = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(root, comparison)) {
            throw new InvalidOperationException($"GitHub asset path '{relativePath}' escapes the static directory.");
        }
        return destination;
    }

    private static void PublishStagingDirectory(string stagingDirectory, string staticDirectory) {
        if (Directory.Exists(staticDirectory)) {
            if (Directory.EnumerateFileSystemEntries(staticDirectory).Any()) {
                throw new IOException($"HttpArena static directory '{staticDirectory}' became non-empty during download.");
            }
            if ((File.GetAttributes(staticDirectory) & FileAttributes.ReparsePoint) != 0) {
                throw new IOException($"HttpArena static directory '{staticDirectory}' is a symbolic link or reparse point.");
            }
            Directory.Delete(staticDirectory, recursive: false);
        }

        Directory.Move(stagingDirectory, staticDirectory);
    }

    private sealed class GitTreeResponse {

        public GitTreeResponse() { }

        [JsonPropertyName("truncated")]
        public bool Truncated { get; set; }

        [JsonPropertyName("tree")]
        public List<GitTreeEntry>? Tree { get; set; }
    }

    private sealed class GitTreeEntry {

        public GitTreeEntry() { }

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    #endregion static dir

    #region helpers

    private static HttpResponse Text(HttpSession session, long value) => session.Response.Text(value.ToString());

    private static int ParseInt(ReadOnlySequence<byte> sequence) {
        if (sequence.IsSingleSegment) {
            Utf8Parser.TryParse(sequence.FirstSpan, out int value, out _);
            return value;
        }

        Span<byte> buffer = stackalloc byte[(int)sequence.Length];
        sequence.CopyTo(buffer);
        Utf8Parser.TryParse(buffer, out int value2, out _);
        return value2;
    }

    #endregion helpers

}
