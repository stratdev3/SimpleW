using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines the path of a directory to create.
    /// </summary>
    internal sealed class FolderRequest {

        /// <summary>
        /// Gets or sets the directory path.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

    }

}
