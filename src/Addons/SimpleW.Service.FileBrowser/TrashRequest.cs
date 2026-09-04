using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class TrashRequest {

        [JsonPropertyName("ids")]
        public List<string>? Ids { get; set; }

        [JsonPropertyName("destinationPath")]
        public string? DestinationPath { get; set; }

    }

}
