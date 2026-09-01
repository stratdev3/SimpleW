using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class UploadFileRequest {

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

    }

}
