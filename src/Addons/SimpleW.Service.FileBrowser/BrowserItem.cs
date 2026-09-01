using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed record BrowserItem(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("modifiedUtc")] DateTime ModifiedUtc
    );

}
