namespace SimpleW.Service.FileBrowser {

    /// <summary>
    /// Associates a client-facing relative path with its validated absolute path.
    /// </summary>
    internal readonly record struct ResolvedPath(string RelativePath, string FullPath);

}
