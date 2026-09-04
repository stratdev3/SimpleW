using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class ExtractRequest {

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("destinationDirectory")]
        public string? DestinationDirectory { get; set; }

        [JsonPropertyName("createDestinationDirectory")]
        public bool CreateDestinationDirectory { get; set; }

    }

}
