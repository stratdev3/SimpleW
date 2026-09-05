using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines a file or directory rename request.
    /// </summary>
    internal sealed class RenameRequest {

        /// <summary>
        /// Gets or sets the path to rename.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets the new entry name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

    }

}
