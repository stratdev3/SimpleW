using System.Dynamic;
using System.Reflection;
using Microsoft.AspNetCore.Html;
using RazorLight;
using RazorLight.Compilation;
using RazorLight.Razor;
using SimpleW.Modules;


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

    /// <summary>
    /// RazorModule
    /// </summary>
    public sealed class RazorModule : IHttpModule {

        /// <summary>
        /// Options
        /// </summary>
        private readonly RazorOptions _options;

        /// <summary>
        /// RazorEngine
        /// </summary>
        private RazorLightEngine? _engine;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public RazorModule(RazorOptions options) {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Install Module in server (called by SimpleW)
        /// </summary>
        /// <param name="server"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Install(SimpleWServer server) {
            if (server.IsStarted) {
                throw new InvalidOperationException("RazorModule must be installed before server start.");
            }

            // init engine once
            _engine = new RazorLightEngineBuilder()
                            .UseProject(new SimpleWRazorProject(_options))
                            .SetOperatingAssembly(typeof(RazorModule).Assembly) // need to specify the assembly to avoid PreserveCompilationContext exception
                            .UseMemoryCachingProvider()
                            .Build();

            // wrap existing handler-result (default is JSON sender) 
            HttpResultHandler next = server.Router.ResultHandler;

            server.ConfigureResultHandler(async (session, result) => {
                // add Razor render
                if (result is ViewResult vr) {
                    await RenderViewAsync(session, vr).ConfigureAwait(false);
                    return;
                }

                await next(session, result).ConfigureAwait(false);
            });
        }

        /// <summary>
        /// Handler
        /// </summary>
        /// <param name="session"></param>
        /// <param name="view"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask RenderViewAsync(HttpSession session, ViewResult view) {
            if (_engine is null) {
                throw new InvalidOperationException("RazorModule not initialized.");
            }

            // RazorLight key == relative path inside ViewsPath
            string key = NormalizeViewName(view.Name);

            string html;
            try {
                // @Model can be anonymous type, DTO (so need Expando !!), whatever.
                ExpandoObject? model = ToExpando(view.Model);
                // @ViewBag
                ExpandoObject viewBag = view.ViewBag ?? new ExpandoObject();
                IDictionary<string, object?> bag = viewBag;
                bag["Html"] = new SimpleHtmlHelper(_engine, _options);
                // render html
                html = await _engine!.CompileRenderAsync(key, model, viewBag).ConfigureAwait(false);
            }
            catch (TemplateCompilationException ex) {
                await session.Response
                             .Status(500)
                             .Html($"<h1>Razor compilation error</h1><pre>{System.Net.WebUtility.HtmlEncode(ex.ToString())}</pre>")
                             .SendAsync().ConfigureAwait(false);
                return;
            }
            catch (Exception ex) {
                await session.Response
                             .Status(500)
                             .Html($"<h1>Razor render error</h1><pre>{System.Net.WebUtility.HtmlEncode(ex.ToString())}</pre>")
                             .SendAsync().ConfigureAwait(false);
                return;
            }

            await session.Response
                         .Status(view.StatusCode)
                         .Html(html, view.ContentType)
                         .SendAsync().ConfigureAwait(false);
        }

        #region helpers

        /// <summary>
        /// ToExpando
        /// </summary>
        /// <param name="model"></param>
        /// <returns></returns>
        private static ExpandoObject ToExpando(object? model) {
            ExpandoObject exp = new();
            IDictionary<string, object?>? dict = exp;

            if (model == null) {
                return exp;
            }

            if (model is ExpandoObject eo) {
                return eo;
            }

            foreach (PropertyInfo p in model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (p.GetIndexParameters().Length != 0) {
                    continue;
                }
                dict[p.Name] = p.GetValue(model);
            }

            return exp;
        }

        /// <summary>
        /// Normalize View Name :
        ///    - "Home" => "Home.cshtml"
        ///    - "Folder\Home" => "Folder/Home.cshtml"
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private string NormalizeViewName(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("View name must not be null or empty.", nameof(name));
            }
            string n = name.Replace('\\', '/').TrimStart('/');
            if (_options.AutoAppendExtension && !n.EndsWith(_options.ViewExtension, StringComparison.OrdinalIgnoreCase)) {
                n += _options.ViewExtension;
            }
            return n;
        }

        /// <summary>
        /// UnderscoreFileName
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        private static string UnderscoreFileName(string key) {
            int slash = key.LastIndexOf('/');
            string dir = slash >= 0 ? key[..(slash + 1)] : string.Empty;
            string file = slash >= 0 ? key[(slash + 1)..] : key;

            if (file.StartsWith('_')) {
                return key;
            }
            return dir + "_" + file;
        }

        /// <summary>
        /// TrimSlashes
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static string TrimSlashes(string s) => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Replace('\\', '/').Trim('/');

        #endregion helpers

        #region RazorLight project with fallbacks

        /// <summary>
        /// SimpleWRazorProject
        /// </summary>
        private sealed class SimpleWRazorProject : RazorLightProject {

            private readonly RazorOptions _o;
            private readonly FileSystemRazorProject _inner;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="options"></param>
            public SimpleWRazorProject(RazorOptions options) {
                _o = options;
                _inner = new FileSystemRazorProject(_o.ViewsPath);
            }

            /// <summary>
            /// GetImportsAsync
            /// </summary>
            /// <param name="templateKey"></param>
            /// <returns></returns>
            public override Task<IEnumerable<RazorLightProjectItem>> GetImportsAsync(string templateKey) {
                return _inner.GetImportsAsync(NormalizeKey(templateKey));
            }

            /// <summary>
            /// GetItemAsync
            /// </summary>
            /// <param name="templateKey"></param>
            /// <returns></returns>
            public override async Task<RazorLightProjectItem> GetItemAsync(string templateKey) {
                string key = NormalizeKey(templateKey);

                // 1) direct
                var item = await _inner.GetItemAsync(key).ConfigureAwait(false);
                if (item.Exists) {
                    return item;
                }

                // 2) Shared/ + Partials/ si pas de folder
                if (!key.Contains('/')) {
                    string sharedKey = $"{TrimSlashes(_o.LayoutsPath)}/{key}";
                    item = await _inner.GetItemAsync(sharedKey).ConfigureAwait(false);
                    if (item.Exists) {
                        return item;
                    }

                    string partialKey = $"{TrimSlashes(_o.PartialsPath)}/{key}";
                    item = await _inner.GetItemAsync(partialKey).ConfigureAwait(false);
                    if (item.Exists) {
                        return item;
                    }
                }

                // 3) underscore convention
                string under = UnderscoreFileName(key);
                if (!string.Equals(under, key, StringComparison.Ordinal)) {
                    item = await _inner.GetItemAsync(under).ConfigureAwait(false);
                    if (item.Exists) {
                        return item;
                    }

                    if (!under.Contains('/')) {
                        string sharedKey = $"{TrimSlashes(_o.LayoutsPath)}/{under}";
                        item = await _inner.GetItemAsync(sharedKey).ConfigureAwait(false);
                        if (item.Exists) {
                            return item;
                        }

                        string partialKey = $"{TrimSlashes(_o.PartialsPath)}/{under}";
                        item = await _inner.GetItemAsync(partialKey).ConfigureAwait(false);
                        if (item.Exists) {
                            return item;
                        }
                    }
                }

                return item; // non existing -> RazorLight throw
            }

            /// <summary>
            /// NormalizeKey
            /// </summary>
            /// <param name="key"></param>
            /// <returns></returns>
            private new string NormalizeKey(string key) {
                if (string.IsNullOrWhiteSpace(key)) {
                    return string.Empty;
                }

                string n = key.Replace('\\', '/').TrimStart('/');

                if (_o.AutoAppendExtension && !n.EndsWith(_o.ViewExtension, StringComparison.OrdinalIgnoreCase)) {
                    n += _o.ViewExtension;
                }

                return n;
            }

        }

        #endregion RazorLight project with fallbacks

        #region HtmlHelper (ASP.NET Core-like)

        /// <summary>
        /// SimpleHtmlHelper
        /// </summary>
        public sealed class SimpleHtmlHelper {

            /// <summary>
            /// Razor Engine
            /// </summary>
            private readonly RazorLightEngine _engine;

            /// <summary>
            /// Options
            /// </summary>
            private readonly RazorOptions _o;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="engine"></param>
            /// <param name="options"></param>
            internal SimpleHtmlHelper(RazorLightEngine engine, RazorOptions options) {
                _engine = engine;
                _o = options;
            }

            /// <summary>
            /// PartialAsync
            /// </summary>
            /// <param name="name"></param>
            /// <returns></returns>
            public Task<IHtmlContent> PartialAsync(string name) => PartialAsync(name, model: null);

            /// <summary>
            /// PartialAsync
            /// </summary>
            /// <param name="name"></param>
            /// <param name="model"></param>
            /// <returns></returns>
            public async Task<IHtmlContent> PartialAsync(string name, object? model) {
                string key = NormalizePartialKey(name);
                string html = await _engine.CompileRenderAsync(key, model).ConfigureAwait(false);
                return new HtmlString(html);
            }

            /// <summary>
            /// NormalizePartialKey
            /// </summary>
            /// <param name="name"></param>
            /// <returns></returns>
            /// <exception cref="ArgumentException"></exception>
            private string NormalizePartialKey(string name) {
                if (string.IsNullOrWhiteSpace(name)) {
                    throw new ArgumentException("Partial name must not be null or empty.", nameof(name));
                }

                string n = name.Replace('\\', '/').TrimStart('/');

                if (_o.AutoAppendExtension && !n.EndsWith(_o.ViewExtension, StringComparison.OrdinalIgnoreCase)) {
                    n += _o.ViewExtension;
                }

                // direct
                if (TemplateExists(n)) {
                    return n;
                }

                // underscore
                string under = UnderscoreFileName(n);
                if (TemplateExists(under)) {
                    return under;
                }

                // Partials/
                string partialBase = TrimSlashes(_o.PartialsPath);
                if (!string.IsNullOrEmpty(partialBase)) {
                    string inPartials = $"{partialBase}/{n}";
                    if (TemplateExists(inPartials)) {
                        return inPartials;
                    }

                    string inPartialsUnder = $"{partialBase}/{under}";
                    if (TemplateExists(inPartialsUnder)) {
                        return inPartialsUnder;
                    }
                }

                // Shared/
                string layoutBase = TrimSlashes(_o.LayoutsPath);
                if (!string.IsNullOrEmpty(layoutBase)) {
                    string inShared = $"{layoutBase}/{n}";
                    if (TemplateExists(inShared)) {
                        return inShared;
                    }

                    string inSharedUnder = $"{layoutBase}/{under}";
                    if (TemplateExists(inSharedUnder)) {
                        return inSharedUnder;
                    }
                }

                // fallback -> RazorLight throw clean error
                return n;
            }

            /// <summary>
            /// TemplateExists
            /// </summary>
            /// <param name="key"></param>
            /// <returns></returns>
            private bool TemplateExists(string key) {
                string relative = key.Replace('/', Path.DirectorySeparatorChar);
                string full = Path.Combine(_o.ViewsPath, relative);
                return File.Exists(full);
            }
        }

        #endregion HtmlHelper (ASP.NET Core-like)

    }

}
