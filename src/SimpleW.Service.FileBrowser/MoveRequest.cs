using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class MoveRequest {

        [JsonPropertyName("sourcePath")]
        public string? SourcePath { get; set; }

        [JsonPropertyName("destinationDirectory")]
        public string? DestinationDirectory { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

    }

}
