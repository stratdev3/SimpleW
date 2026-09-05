using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Defines a request targeting entries currently stored in the trash.
    /// </summary>
    internal sealed class TrashRequest {

        /// <summary>
        /// Gets or sets the trash entry identifiers.
        /// </summary>
        [JsonPropertyName("ids")]
        public List<string>? Ids { get; set; }

        /// <summary>
        /// Gets or sets an optional destination used while restoring an entry.
        /// </summary>
        [JsonPropertyName("destinationPath")]
        public string? DestinationPath { get; set; }

    }

}
