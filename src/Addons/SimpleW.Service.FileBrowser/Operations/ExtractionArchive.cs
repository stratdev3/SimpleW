using System.IO.Compression;

namespace SimpleW.Service.FileBrowser {

    /// <summary>Common metadata and stream ownership for ZIP and ordered RAR extraction.</summary>
    internal sealed class ExtractionArchive : IDisposable {
        internal sealed record Entry(string FullName, bool IsDirectory, long Length, Action<Stream, CancellationToken> ExtractTo);
        private readonly IReadOnlyList<Stream> streams;
        private readonly IDisposable? archive;
        internal IReadOnlyList<Entry> Entries { get; }
        internal IReadOnlyList<ResolvedPath> Sources { get; }

        private ExtractionArchive(IReadOnlyList<ResolvedPath> sources, IReadOnlyList<Stream> streams, IDisposable? archive, IReadOnlyList<Entry> entries) {
            Sources = sources;
            this.streams = streams;
            this.archive = archive;
            Entries = entries;
        }

        internal static string[] ResolveRarVolumes(string firstPath, int maxVolumes, CancellationToken cancellationToken) {
            try { return Unrar.RarVolumes.Resolve(firstPath, maxVolumes, cancellationToken).ToArray(); }
            catch (Unrar.RarException exception) { throw RarError(exception); }
        }

        // Takes ownership of the streams, including when construction fails.
        internal static ExtractionArchive Open(IReadOnlyList<ResolvedPath> sources, IReadOnlyList<Stream> streams, bool zip, FileBrowserOptions options, CancellationToken cancellationToken) {
            IDisposable? archive = null;
            try {
                List<Entry> entries = new();
                if (zip) {
                    ZipArchive reader = new(streams[0], ZipArchiveMode.Read, leaveOpen: true);
                    archive = reader;
                    if (reader.Entries.Count > options.MaxArchiveEntries) { throw new InvalidDataException("archive_too_many_entries"); }
                    foreach (ZipArchiveEntry entry in reader.Entries) {
                        cancellationToken.ThrowIfCancellationRequested();
                        bool isDirectory = entry.FullName.Replace('\\', '/').EndsWith('/') || string.IsNullOrEmpty(entry.Name);
                        entries.Add(new(entry.FullName, isDirectory, entry.Length, (output, token) => {
                            token.ThrowIfCancellationRequested();
                            if (isDirectory) { return; }
                            using Stream input = entry.Open();
                            byte[] buffer = new byte[81920];
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                                token.ThrowIfCancellationRequested();
                                output.Write(buffer, 0, read);
                            }
                        }));
                    }
                }
                else {
                    Unrar.RarLimits limits = new() {
                        MaxEntries = options.MaxArchiveEntries,
                        MaxVolumes = options.MaxArchiveEntries,
                        MaxFileBytes = options.MaxExtractedFileBytes,
                        MaxTotalBytes = options.MaxExtractedBytes
                    };
                    Unrar.RarArchive reader = new(streams, sources[0].FullPath.EndsWith(".001", StringComparison.OrdinalIgnoreCase), limits, cancellationToken);
                    foreach (Unrar.RarEntry entry in reader.Entries) {
                        int index = entries.Count;
                        entries.Add(new(entry.Name, entry.IsDirectory, entry.Size, (output, token) => {
                            try { reader.ExtractTo(index, output, token); }
                            catch (Unrar.RarException exception) { throw RarError(exception); }
                        }));
                    }
                }
                return new(sources, streams, archive, entries);
            }
            catch (Exception exception) {
                try { archive?.Dispose(); }
                finally { foreach (Stream stream in streams) { stream.Dispose(); } }
                if (exception is Unrar.RarException rarException) { throw RarError(rarException); }
                throw;
            }
        }

        private static InvalidDataException RarError(Unrar.RarException exception) => new(exception.Failure switch {
            Unrar.RarFailure.Encrypted => "encrypted_archive_unsupported",
            Unrar.RarFailure.SecondaryVolume => "rar_first_volume_required",
            Unrar.RarFailure.Incomplete => "incomplete_archive",
            Unrar.RarFailure.Unsupported => "unsupported_archive_method",
            Unrar.RarFailure.Limit => "archive_limit_exceeded",
            _ => "invalid_archive"
        }, exception);

        public void Dispose() {
            try { archive?.Dispose(); }
            finally { foreach (Stream stream in streams) { stream.Dispose(); } }
        }
    }
}

