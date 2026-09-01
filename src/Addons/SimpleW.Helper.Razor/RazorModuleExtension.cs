namespace SimpleW.Helper.Razor {

    /// <summary>
    /// RazorModuleExtension
    /// </summary>
    /// <example>
    /// server.UseRazorModule(o => {
    ///     //o.ViewsPath = "Views";
    /// });
    /// </example>
    public static class RazorModuleExtension {

        /// <summary>
        /// Use Razor Module
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseRazorModule(this SimpleWServer server, Action<RazorOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            RazorOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new RazorModule(options));
            return server;
        }

    }

}
