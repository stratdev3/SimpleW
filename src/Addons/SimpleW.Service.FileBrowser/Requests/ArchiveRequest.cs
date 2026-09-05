using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines the source paths and destination of an archive creation request.
    /// </summary>
    internal sealed class ArchiveRequest {

        /// <summary>
        /// Gets or sets the files and directories to archive.
        /// </summary>
        [JsonPropertyName("paths")]
        public List<string>? Paths { get; set; }

        /// <summary>
        /// Gets or sets the relative path of the archive to create.
        /// </summary>
        [JsonPropertyName("destinationPath")]
        public string? DestinationPath { get; set; }

    }

}
