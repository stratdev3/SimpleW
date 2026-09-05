namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Represents a half-open byte range received for a chunked upload.
    /// </summary>
    internal readonly record struct ReceivedRange(long Start, long End);

}
