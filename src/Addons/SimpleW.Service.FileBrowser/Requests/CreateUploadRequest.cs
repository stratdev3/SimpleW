using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines the files declared when an upload session is created.
    /// </summary>
    internal sealed class CreateUploadRequest {

        /// <summary>
        /// Gets or sets the files included in the upload session.
        /// </summary>
        [JsonPropertyName("files")]
        public List<UploadFileRequest>? Files { get; set; }

    }

}
