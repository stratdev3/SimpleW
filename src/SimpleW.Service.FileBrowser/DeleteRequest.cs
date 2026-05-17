using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class DeleteRequest {

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("paths")]
        public List<string>? Paths { get; set; }

    }

}
