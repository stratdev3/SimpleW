namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Contains the paths and optional event payloads produced by a queued operation.
    /// </summary>
    internal sealed record OperationResult(string[] ChangedPaths, object? Payload = null, object[]? UploadCompletedPayloads = null);

}
