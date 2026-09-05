using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines a file or directory move request.
    /// </summary>
    internal sealed class MoveRequest {

        /// <summary>
        /// Gets or sets the source path.
        /// </summary>
        [JsonPropertyName("sourcePath")]
        public string? SourcePath { get; set; }

        /// <summary>
        /// Gets or sets the destination directory.
        /// </summary>
        [JsonPropertyName("destinationDirectory")]
        public string? DestinationDirectory { get; set; }

        /// <summary>
        /// Gets or sets an optional destination name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

    }

}
