using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class ArchiveRequest {

        [JsonPropertyName("paths")]
        public List<string>? Paths { get; set; }

        [JsonPropertyName("destinationPath")]
        public string? DestinationPath { get; set; }

    }

}
