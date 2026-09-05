using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Describes one file declared by an upload creation request.
    /// </summary>
    internal sealed class UploadFileRequest {

        /// <summary>
        /// Gets or sets the destination path of the file.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets the expected file size in bytes.
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }

    }

}
