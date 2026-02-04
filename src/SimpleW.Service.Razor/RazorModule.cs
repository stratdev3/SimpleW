using System.Dynamic;
using System.Reflection;
using RazorLight;
using RazorLight.Compilation;
using SimpleW.Modules;


namespace SimpleW.Service.Razor {

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
        /// File extension for templates.
        /// </summary>
        public string ViewExtension { get; set; } = ".cshtml";

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
                            .UseFileSystemProject(_options.ViewsPath)
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
                ExpandoObject? viewBag = view.ViewBag;
                // render html
                html = await _engine!.CompileRenderAsync(view.Name, model, viewBag).ConfigureAwait(false);
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
            if (!n.EndsWith(_options.ViewExtension, StringComparison.OrdinalIgnoreCase)) {
                n += _options.ViewExtension;
            }
            return n;
        }

        #endregion helpers

    }

}
