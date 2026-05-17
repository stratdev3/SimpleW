namespace SimpleW.Service.FileBrowser {

    internal sealed record OperationResult(string[] ChangedPaths, object? Payload = null, object[]? UploadCompletedPayloads = null);

}
