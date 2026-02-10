using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;


namespace SimpleW.Helper.Swagger {

    /// <summary>
    /// Swagger/OpenAPI helper for SimpleW.
    /// Generates an OpenAPI 3.0 JSON document from the currently registered routes
    ///
    /// You call it from your own routes so YOU control security/auth.
    ///
    /// Example:
    /// server.MapGet("/swagger.json", static (HttpSession session) =>
    ///     Swagger.Json(session));
    ///
    /// server.MapGet("/admin/swagger", static (HttpSession session) => {
    ///     // your security here
    ///     // if (!IsAdmin(session)) return session.Response.Status(403).Text("Forbidden");
    ///     return Swagger.Ui(session, "/swagger.json");
    /// });
    /// </summary>
    public static class Swagger {

        /// <summary>
        /// Build and write the OpenAPI 3.0 JSON document into session.Response, then return it.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static HttpResponse Json(HttpSession session, Action<SwaggerOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(session);

            SwaggerOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            object doc = OpenApiBuilder.Build(session.Server, session, options);

            session.Response.Json(doc);
            return session.Response;
        }

        /// <summary>
        /// Write a Swagger UI HTML page into session.Response, then return it.
        /// swaggerJsonUrl can be relative (recommended): "/swagger.json"
        /// </summary>
        /// <param name="session"></param>
        /// <param name="swaggerJsonUrl"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static HttpResponse Ui(HttpSession session, string swaggerJsonUrl, Action<SwaggerOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(session);

            if (string.IsNullOrWhiteSpace(swaggerJsonUrl)) {
                swaggerJsonUrl = "/swagger.json";
            }

            SwaggerOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            string html = options.UiHtmlFactory?.Invoke(swaggerJsonUrl) ?? DefaultUiHtml(options.Title, swaggerJsonUrl);

            session.Response.Html(html);
            return session.Response;
        }

        /// <summary>
        /// Options for Swagger helper
        /// </summary>
        public sealed class SwaggerOptions {

            /// <summary>
            /// OpenAPI title
            /// </summary>
            public string Title { get; set; } = "SimpleW API";

            /// <summary>
            /// OpenAPI version string (info.version)
            /// </summary>
            public string Version { get; set; } = "v1";

            /// <summary>
            /// Optional description
            /// </summary>
            public string? Description { get; set; }

            /// <summary>
            /// Filter which routes appear in swagger (null => all routes)
            /// </summary>
            public Func<Router.RouteInfo, bool>? RouteFilter { get; set; }

            /// <summary>
            /// Best effort: scan Controller methods to infer query params types
            /// </summary>
            public bool ScanControllersForParameters { get; set; } = true;

            /// <summary>
            /// Customize swagger UI HTML (advanced)
            /// </summary>
            public Func<string /*swaggerJsonUrl*/, string /*html*/>? UiHtmlFactory { get; set; }

            /// <summary>
            /// Check Properties and return
            /// </summary>
            /// <returns></returns>
            internal SwaggerOptions ValidateAndNormalize() {
                Title = string.IsNullOrWhiteSpace(Title) ? "SimpleW API" : Title.Trim();
                Version = string.IsNullOrWhiteSpace(Version) ? "v1" : Version.Trim();
                return this;
            }

        }

        /// <summary>
        /// Default UI
        /// </summary>
        /// <param name="title"></param>
        /// <param name="swaggerJsonUrl"></param>
        /// <returns></returns>
        private static string DefaultUiHtml(string title, string swaggerJsonUrl) {
            return $$"""
                        <!doctype html>
                        <html lang="en">
                        <head>
                            <meta charset="utf-8" />
                            <meta name="viewport" content="width=device-width, initial-scale=1" />
                            <title>{{EscapeHtml(title)}} - Swagger UI</title>
                            <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
                            <style>
                            body {margin: 0; background: #0b1020; }
                            .topbar { display:none; }
                            </style>
                        </head>
                        <body>
                            <div id="swagger-ui"></div>
                            <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
                            <script>
                            window.onload = function() {
                                window.ui = SwaggerUIBundle({
                                    url: "{{swaggerJsonUrl}}",
                                    dom_id: '#swagger-ui',
                                    deepLinking: true,
                                    presets: [SwaggerUIBundle.presets.apis],
                                    layout: "BaseLayout"
                                });
                            };
                            </script>
                        </body>
                        </html>
                    """;
        }

        /// <summary>
        /// Escapge HTML char
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        private static string EscapeHtml(string s) => s.Replace("&", "&amp;")
                                                       .Replace("<", "&lt;")
                                                       .Replace(">", "&gt;")
                                                       .Replace("\"", "&quot;")
                                                       .Replace("'", "&#39;");

        /// <summary>
        /// OpenApiBuilder
        /// </summary>
        private static class OpenApiBuilder {

            /// <summary>
            /// Match "{param}" in already-openapi path templates.
            /// </summary>
            private static readonly Regex OpenApiParamRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);

            /// <summary>
            /// // Match SimpleW segments :id and :path*
            /// </summary>
            private static readonly Regex SimpleWParamRegex = new(@":([A-Za-z0-9_]+)\*?", RegexOptions.Compiled);

            /// <summary>
            /// Builder
            /// </summary>
            /// <param name="server"></param>
            /// <param name="session"></param>
            /// <param name="options"></param>
            /// <returns></returns>
            public static object Build(SimpleWServer server, HttpSession session, SwaggerOptions options) {

                // Build base server URL (best effort)
                string scheme = session.TransportStream is System.Net.Security.SslStream ? "https" : "http";
                string host = session.Request.Headers.Host ?? "localhost";
                string serverUrl = $"{scheme}://{host}";

                // routes -> paths
                IEnumerable<Router.RouteInfo> allRoutes = server.Router.Routes;

                if (options.RouteFilter != null) {
                    allRoutes = allRoutes.Where(options.RouteFilter);
                }

                // Optional: enrich operations with query params inferred from Controllers.
                // We'll build a lookup (method+pathTemplate => ParameterSpec[])
                Dictionary<(string method, string path), List<ParameterSpec>>? controllerParams = null;
                if (options.ScanControllersForParameters) {
                    controllerParams = TryBuildControllerParamLookup();
                }

                // OpenAPI "paths" object
                Dictionary<string, object> paths = new(StringComparer.Ordinal);

                foreach (Router.RouteInfo r in allRoutes) {

                    string openApiPath = ToOpenApiPathTemplate(r.Path);

                    if (!paths.TryGetValue(openApiPath, out object? pathItemObj) || pathItemObj is not Dictionary<string, object> pathItem) {
                        pathItem = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        paths[openApiPath] = pathItem;
                    }

                    string methodKey = r.Method.ToLowerInvariant();

                    // parameters: path params + (optional) query params from controller signature
                    List<object> parameters = new();

                    foreach (string p in ExtractOpenApiPathParams(openApiPath)) {
                        parameters.Add(new Dictionary<string, object> {
                            ["name"] = p,
                            ["in"] = "path",
                            ["required"] = true,
                            ["schema"] = new Dictionary<string, object> { ["type"] = "string" }
                        });
                    }

                    if (controllerParams != null && controllerParams.TryGetValue((r.Method, r.Path), out var qps)) {
                        foreach (var qp in qps) {
                            parameters.Add(new Dictionary<string, object> {
                                ["name"] = qp.Name,
                                ["in"] = qp.In,
                                ["required"] = qp.Required,
                                ["schema"] = new Dictionary<string, object> { ["type"] = qp.SchemaType }
                            });
                        }
                    }

                    // responses (minimal)
                    Dictionary<string, object> responses = new Dictionary<string, object> {
                        ["200"] = new Dictionary<string, object> { ["description"] = "Success" }
                    };

                    // operation
                    Dictionary<string, object> op = new Dictionary<string, object> {
                        ["summary"] = r.Description ?? $"{r.Method} {r.Path}",
                        ["operationId"] = MakeOperationId(r.Method, openApiPath),
                        ["responses"] = responses
                    };

                    if (parameters.Count > 0) {
                        op["parameters"] = parameters;
                    }

                    // Tag by first segment to make UI nicer
                    string tag = FirstTagFromPath(openApiPath);
                    op["tags"] = new[] { tag };

                    pathItem[methodKey] = op;
                }

                // Build final OpenAPI doc
                Dictionary<string, object> doc = new Dictionary<string, object> {
                    ["openapi"] = "3.0.3",
                    ["info"] = new Dictionary<string, object> {
                        ["title"] = options.Title,
                        ["version"] = options.Version,
                    },
                    ["servers"] = new[] {
                        new Dictionary<string, object> { ["url"] = serverUrl }
                    },
                    ["paths"] = paths
                };

                if (!string.IsNullOrWhiteSpace(options.Description)) {
                    ((Dictionary<string, object>)doc["info"])["description"] = options.Description!;
                }

                return doc;
            }

            private static string MakeOperationId(string method, string openApiPath) {
                // GET_/api/users/{id} => get_api_users_id
                var sb = new StringBuilder();
                sb.Append(method.ToLowerInvariant());
                sb.Append('_');

                foreach (char c in openApiPath) {
                    if (char.IsLetterOrDigit(c)) {
                        sb.Append(char.ToLowerInvariant(c));
                    }
                    else if (c == '{' || c == '}' || c == '/') {
                        sb.Append('_');
                    }
                    else if (c == '-') {
                        sb.Append('_');
                    }
                }

                string s = sb.ToString();
                while (s.Contains("__", StringComparison.Ordinal)) {
                    s = s.Replace("__", "_", StringComparison.Ordinal);
                }
                return s.Trim('_');
            }

            private static string FirstTagFromPath(string openApiPath) {
                // "/api/users/{id}" => "api"
                if (string.IsNullOrEmpty(openApiPath) || openApiPath == "/") {
                    return "default";
                }
                int i = 0;
                while (i < openApiPath.Length && openApiPath[i] == '/') {
                    i++;
                }
                int start = i;
                while (i < openApiPath.Length && openApiPath[i] != '/') {
                    i++;
                }
                if (i <= start) {
                    return "default";
                }
                string seg = openApiPath[start..i];
                return string.IsNullOrWhiteSpace(seg) ? "default" : seg;
            }

            /// <summary>
            /// Convert a SimpleW route template to an OpenAPI path template.
            /// - ":id" becomes "{id}"
            /// - ":path*" becomes "{path}"
            /// - "*" becomes "{wildcard}"
            /// </summary>
            /// <param name="simpleWPath"></param>
            /// <returns></returns>
            private static string ToOpenApiPathTemplate(string simpleWPath) {
                if (string.IsNullOrWhiteSpace(simpleWPath)) {
                    return "/";
                }

                // ensure starts with "/"
                string p = simpleWPath.StartsWith("/", StringComparison.Ordinal) ? simpleWPath : "/" + simpleWPath;

                // replace wildcard segment "*" (SimpleW pattern uses "/*" or "/something/*")
                p = p.Replace("/*", "/{wildcard}", StringComparison.Ordinal);

                // :name and :name*
                p = SimpleWParamRegex.Replace(p, m => "{" + m.Groups[1].Value + "}");

                // remove trailing slashes (except "/")
                if (p.Length > 1 && p.EndsWith("/", StringComparison.Ordinal)) {
                    p = p.TrimEnd('/');
                }
                return p;
            }

            private static IEnumerable<string> ExtractOpenApiPathParams(string openApiPath) {
                foreach (Match m in OpenApiParamRegex.Matches(openApiPath)) {
                    string name = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(name)) {
                        yield return name;
                    }
                }
            }

            //
            // Optional enrichment: scan controllers for query param types
            //

            private readonly record struct ParameterSpec(string Name, string In, bool Required, string SchemaType);

            private static Dictionary<(string method, string path), List<ParameterSpec>>? TryBuildControllerParamLookup() {
                try {
                    var lookup = new Dictionary<(string method, string path), List<ParameterSpec>>();

                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                        Type[] types;
                        try { types = asm.GetTypes(); }
                        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }

                        foreach (Type t in types) {
                            if (t.IsAbstract) {
                                continue;
                            }
                            if (!typeof(Controller).IsAssignableFrom(t)) {
                                continue;
                            }

                            RouteAttribute? classRoute = t.GetCustomAttribute<RouteAttribute>(inherit: true);
                            string controllerPrefix = classRoute?.Path ?? string.Empty;

                            foreach (var mi in t.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                                RouteAttribute[] attrs = mi.GetCustomAttributes<RouteAttribute>(inherit: true).ToArray();
                                if (attrs.Length == 0) {
                                    continue;
                                }

                                foreach (var attr in attrs.Where(a => !string.IsNullOrWhiteSpace(a.Method) && a.Method != "*")) {

                                    string fullPath;
                                    if (attr.IsAbsolutePath) {
                                        fullPath = string.IsNullOrEmpty(attr.Path) ? "/" : attr.Path;
                                    }
                                    else {
                                        string path = (controllerPrefix ?? string.Empty) + (attr.Path ?? string.Empty);
                                        fullPath = string.IsNullOrEmpty(path) ? "/" : path;
                                    }

                                    HashSet<string> pathParamNames = new(StringComparer.OrdinalIgnoreCase);
                                    foreach (Match m in SimpleWParamRegex.Matches(fullPath)) {
                                        pathParamNames.Add(m.Groups[1].Value);
                                    }

                                    List<ParameterSpec> specs = new();

                                    foreach (ParameterInfo p in mi.GetParameters()) {
                                        if (p.ParameterType == typeof(HttpSession)) {
                                            continue;
                                        }
                                        if (string.IsNullOrWhiteSpace(p.Name)) {
                                            continue;
                                        }

                                        bool isPath = pathParamNames.Contains(p.Name);
                                        string location = isPath ? "path" : "query";

                                        bool required = isPath ? true : (!p.HasDefaultValue && !IsNullable(p));
                                        string schemaType = ToOpenApiScalar(p.ParameterType);

                                        specs.Add(new ParameterSpec(p.Name, location, required, schemaType));
                                    }

                                    if (specs.Count > 0) {
                                        // Router.RouteInfo.Path uses SimpleW-style templates (with :params) when mapped.
                                        var key = (attr.Method, fullPath);
                                        if (!lookup.TryGetValue(key, out var list)) {
                                            lookup[key] = list = new List<ParameterSpec>();
                                        }
                                        list.AddRange(specs);
                                    }
                                }
                            }
                        }
                    }

                    return lookup;
                }
                catch {
                    return null;
                }
            }

            private static bool IsNullable(ParameterInfo p) {
                Type t = p.ParameterType;
                if (!t.IsValueType) {
                    // Reference types: try to detect nullable annotations.
                    // If metadata missing => assume nullable (safe).
                    var nullableAttr = p.CustomAttributes.FirstOrDefault(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");
                    if (nullableAttr == null) {
                        return true;
                    }
                    if (nullableAttr.ConstructorArguments.Count == 1) {
                        var arg = nullableAttr.ConstructorArguments[0];
                        if (arg.ArgumentType == typeof(byte) && arg.Value is byte b) {
                            // 2 means nullable, 1 means not-null
                            return b == 2;
                        }
                    }
                    return true;
                }
                return Nullable.GetUnderlyingType(t) != null;
            }

            private static string ToOpenApiScalar(Type t) {
                t = Nullable.GetUnderlyingType(t) ?? t;

                if (t == typeof(string) || t == typeof(char)) {
                    return "string";
                }
                if (t == typeof(bool)) {
                    return "boolean";
                }

                if (t == typeof(byte) || t == typeof(sbyte)
                    || t == typeof(short) || t == typeof(ushort)
                    || t == typeof(int) || t == typeof(uint)
                    || t == typeof(long) || t == typeof(ulong)
                ) {
                    return "integer";
                }

                if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) {
                    return "number";
                }

                if (t == typeof(Guid)) {
                    return "string";
                }
                if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) {
                    return "string";
                }

                // fallback
                return "string";
            }

        }
    }

}
