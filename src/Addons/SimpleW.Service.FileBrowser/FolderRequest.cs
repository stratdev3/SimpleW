using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class FolderRequest {

        [JsonPropertyName("path")]
        public string? Path { get; set; }

    }

}
