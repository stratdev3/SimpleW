using System.Text.Json.Serialization;

namespace SimpleW.Service.FileBrowser {

    internal sealed class CreateUploadRequest {

        [JsonPropertyName("files")]
        public List<UploadFileRequest>? Files { get; set; }

    }

}
