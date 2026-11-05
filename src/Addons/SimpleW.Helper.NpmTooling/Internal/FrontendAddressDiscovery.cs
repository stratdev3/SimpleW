using System.Text.RegularExpressions;


namespace SimpleW.Helper.NpmTooling.Internal {

    /// <summary>
    /// Collects local HTTP(S) URLs announced by the managed development script, not arbitrary log links.
    /// </summary>
    internal sealed class FrontendAddressDiscovery {
        private static readonly Regex TerminalCodes = new(
            @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\))",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        private static readonly Regex Announcement = new(
            @"(?:^|\s)(?:(?:Local|Loopback)\s*:|(?:listening|running|started|available|ready)\s+(?:on|at)\s*:?)\s*(?<urls>https?://.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        private static readonly Regex Urls = new(
            "https?://[^\\s<>\"'`,;]+",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        private readonly object _gate = new();
        private readonly List<Uri> _candidates = new();

        /// <summary>
        /// Observes stdout or stderr, stripping terminal formatting and retaining at most sixteen unique URLs.
        /// </summary>
        /// <param name="line">One line from the development script, never dependency installation output.</param>
        public void Observe(string line) {
            if (line.Length > 8192) return;
            Match announcement = Announcement.Match(TerminalCodes.Replace(line, string.Empty));
            if (!announcement.Success) return;
            foreach (Match match in Urls.Matches(announcement.Groups["urls"].Value)) {
                string value = match.Value.TrimEnd('.', ')', '*');
                if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url)
                    || url.Scheme is not ("http" or "https") || url.Port == 0
                    || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) {
                    continue;
                }
                // Wildcard bind addresses are not browser destinations. Preserve IPv4/IPv6 and the base path.
                if (url.Host == "0.0.0.0") url = new UriBuilder(url) { Host = "127.0.0.1" }.Uri;
                else if (url.Host == "[::]") url = new UriBuilder(url) { Host = "[::1]" }.Uri;
                if (!url.IsLoopback) continue;
                lock (_gate) {
                    if (_candidates.Count < 16 && !_candidates.Contains(url)) _candidates.Add(url);
                }
            }
        }

        /// <summary>
        /// Returns a stable snapshot while stdout and stderr continue to arrive concurrently.
        /// </summary>
        /// <returns>Distinct candidate URLs in announcement order.</returns>
        public Uri[] Snapshot() {
            lock (_gate) return _candidates.ToArray();
        }

    }

}
