namespace SimpleW.Helper.Razor {

    /// <summary>
    /// RazorOptions
    /// </summary>
    public sealed class RazorOptions {

        /// <summary>
        /// Base directory for views (default: "./Views").
        /// </summary>
        public string ViewsPath { get; set; } = "Views";

        /// <summary>
        /// Base directory for layouts (default: "./Shared").
        /// </summary>
        public string LayoutsPath { get; set; } = "Shared";

        /// <summary>
        /// Base directory for partials (default: "./Partials").
        /// </summary>
        public string PartialsPath { get; set; } = "Partials";

        /// <summary>
        /// File extension for templates.
        /// </summary>
        public string ViewExtension { get; set; } = ".cshtml";

        /// <summary>
        /// Auto Append File Extension
        /// </summary>
        public bool AutoAppendExtension { get; set; } = true;

    }

}
