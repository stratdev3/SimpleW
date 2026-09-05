using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Describes a file-system entry returned by the listing endpoint.
    /// </summary>
    internal sealed record BrowserItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("modifiedUtc")] DateTime ModifiedUtc
    );

}
