using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines an archive extraction request.
    /// </summary>
    internal sealed class ExtractRequest {

        /// <summary>
        /// Gets or sets the archive path.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets the directory receiving the extracted entries.
        /// </summary>
        [JsonPropertyName("destinationDirectory")]
        public string? DestinationDirectory { get; set; }

        /// <summary>
        /// Gets or sets whether the destination directory must be created for this extraction.
        /// </summary>
        [JsonPropertyName("createDestinationDirectory")]
        public bool CreateDestinationDirectory { get; set; }

    }

}
