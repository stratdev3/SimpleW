using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines one or more paths to move to the trash.
    /// </summary>
    internal sealed class DeleteRequest {

        /// <summary>
        /// Gets or sets the legacy single path to delete.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets the paths to delete.
        /// </summary>
        [JsonPropertyName("paths")]
        public List<string>? Paths { get; set; }

    }

}
