using System.Diagnostics;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Stores the resolved Node executable and npm CLI paths used by supervised processes.
    /// </summary>
    /// <param name="Node">Absolute path to the Node executable.</param>
    /// <param name="NpmCli">Path to npm-cli.js, invoked through Node.</param>
    internal sealed record NodeToolchain(string Node, string NpmCli) {
        /// <summary>
        /// Resolves Node and npm paths and verifies that Node is version 22.12 or later.
        /// </summary>
        /// <param name="options">Optional explicit paths that override tool discovery.</param>
        /// <param name="token">Cancellation token for the Node version check.</param>
        /// <returns>The toolchain to use for dependency installation and frontend startup.</returns>
        public static async Task<NodeToolchain> FindAsync(NpmSpaOptions options, CancellationToken token) {
            string node = options.NodePath == null
                                ? FindOnPath(OperatingSystem.IsWindows() ? "node.exe" : "node")
                                : Path.GetFullPath(options.NodePath);

            if (!File.Exists(node)) {
                throw new FileNotFoundException("Node >= 22.12 is required. Install Node.js or configure Spa.NodePath.", node);
            }
            string npmCli = options.NpmCliPath == null ? FindNpm(node) : Path.GetFullPath(options.NpmCliPath);
            if (!File.Exists(npmCli)) {
                throw new FileNotFoundException("npm-cli.js was not found. Install npm or configure Spa.NpmCliPath.", npmCli);
            }

            ProcessStartInfo info = new(node) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add("--version");
            using Process process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start Node.");
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string version = (await stdout.ConfigureAwait(false)).Trim().TrimStart('v');
                string error = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0 || !Version.TryParse(version, out Version? parsed) || parsed < new Version(22, 12)) {
                    throw new InvalidOperationException($"Node >= 22.12 is required. Found '{version}'. {error}");
                }
            }
            finally {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                }
            }
            return new(node, npmCli);
        }

        /// <summary>
        /// Finds the first matching executable file in the process PATH.
        /// </summary>
        /// <param name="name">Platform-specific executable filename.</param>
        /// <returns>The absolute executable path.</returns>
        private static string FindOnPath(string name) {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
                if (string.IsNullOrWhiteSpace(directory)) {
                    continue;
                }
                string path = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(path)) {
                    return Path.GetFullPath(path);
                }
            }
            throw new FileNotFoundException($"'{name}' was not found on PATH. Install Node >= 22.12 and restart the IDE.");
        }

        /// <summary>
        /// Locates npm-cli.js using the Node installation, npm symlinks and common layouts on PATH.
        /// </summary>
        /// <param name="node">Resolved Node executable path.</param>
        /// <returns>The npm CLI JavaScript file path.</returns>
        private static string FindNpm(string node) {
            List<string> directories = [Path.GetDirectoryName(node)!];
            FileSystemInfo? realNode = new FileInfo(node).ResolveLinkTarget(returnFinalTarget: true);
            if (realNode != null) {
                directories.Add(Path.GetDirectoryName(realNode.FullName)!);
            }

            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) {
                if (!string.IsNullOrWhiteSpace(directory)) {
                    directories.Add(directory.Trim('"'));
                }
            }
            foreach (string directory in directories.Distinct()) {
                string npm = Path.Combine(directory, "npm");
                if (File.Exists(npm)) {
                    FileSystemInfo? target = new FileInfo(npm).ResolveLinkTarget(returnFinalTarget: true);
                    if (target?.Name == "npm-cli.js") {
                        return target.FullName;
                    }
                }
                foreach (string relative in new[] { "node_modules/npm/bin/npm-cli.js", "../lib/node_modules/npm/bin/npm-cli.js", "../share/nodejs/npm/bin/npm-cli.js" }) {
                    string candidate = Path.GetFullPath(Path.Combine(directory, relative));
                    if (File.Exists(candidate)) {
                        return candidate;
                    }
                }
            }
            throw new FileNotFoundException("npm-cli.js was not found. Configure Spa.NpmCliPath for your Node installation.");
        }

    }

}
