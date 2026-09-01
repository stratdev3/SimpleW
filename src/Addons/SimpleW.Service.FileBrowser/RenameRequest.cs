using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class RenameRequest {

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

    }

}
