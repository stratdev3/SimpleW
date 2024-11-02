using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Route class
    /// </summary>
    public partial class Route {

        #region properties

        /// <summary>
        /// The Raw URL from Request
        /// </summary>
        public string RawUrl { get; internal set; }

        /// <summary>
        /// The Http Method "GET", "POST"...
        /// Extract from RawUrl
        /// </summary>
        public string Method { get; internal set; }

        /// <summary>
        /// The Uri sanitized and extract from RawUrl
        /// </summary>
        public Uri Url { get; internal set; }

        /// <summary>
        /// Return true if RawURL ending with "/"
        /// </summary>
        public bool hasEndingSlash => RawUrl.EndsWith("/");

        /// <summary>
        /// The ControllerMethodExecutor
        /// </summary>
        public ControllerMethodExecutor Handler { get; internal set; }

        /// <summary>
        /// The RouteAttribute
        /// </summary>
        public readonly RouteAttribute Attribute;

        #endregion properties

        #region constructor

        /// <summary>
        /// Create Route from HttpRequest
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        public Route(HttpRequest request) {
            Method = request.Method;
            RawUrl = FQURL(request);
            ParseHttpRequest();
        }

        /// <summary>
        /// Create Route from RouteAttribute
        /// </summary>
        /// <param name="attribute">The RouteAttribute attribute.</param>
        /// <param name="handler">The ControllerMethodExecutor handler.</param>
        public Route(RouteAttribute attribute, ControllerMethodExecutor handler) {
            Method = attribute.Method;
            RawUrl = Uri.UnescapeDataString(attribute.Path);
            Attribute = attribute;
            Handler = handler;
            ParseRoute();
        }

        /// <summary>
        /// Create Route manually
        /// </summary>
        /// <param name="method">The http method.</param>
        /// <param name="url">The route url</param>
        /// <param name="handler">The route handler</param>
        public Route(string method, string url, ControllerMethodExecutor handler) {
            Method = method;
            RawUrl = Uri.UnescapeDataString(url);
            Handler = handler;
            ParseRoute();
        }

        #endregion constructor

        #region parse

        private readonly List<string> parameters_name = new();

        /// <summary>
        /// The Regex use to parse parameter depending the current RouteAttribute
        /// </summary>
        public Regex regex { get; private set; }

        /// <summary>
        /// Build time Regex to look for parameter in routePath
        /// </summary>
        /// <returns></returns>
        [GeneratedRegex("{([^}]+)}")]
        private static partial Regex ParameterRegex();

        /// <summary>
        /// Parse Route
        /// </summary>
        private void ParseRoute() {

            // url
            int pos = RawUrl.IndexOf("?");
            string relativePath = pos > 0 ? RawUrl[..pos] : RawUrl;

            // parse RouteAttribute looking for "{parameter}"
            foreach (string parameter in ParameterRegex().Matches(relativePath)
                                                         .Where(m => m.Success)
                                                         .Select(m => m.Groups[1].Value)
            ) {
                // check for uniqueness
                if (!parameters_name.Contains(parameter)) {
                    parameters_name.Add(parameter);
                }
            }

            // Construct regex startings with relativePath
            string pattern = relativePath;

            // the wildcard "*" must be convert to a valid regexp
            pattern = pattern.Replace("*", "(.*)");

            // regexp also contains RouteAttribute parameters
            if (parameters_name.Count > 0) {
                foreach (ParameterInfo parameter in Handler.Parameters.Keys) {
                    pattern = pattern.Replace("{" + parameter.Name + "}", "([^/]+)");
                }
            }
            pattern = "^" + pattern + "$";
            regex = new Regex(pattern);
        }

        /// <summary>
        /// Parse HttpRequest
        /// </summary>
        private void ParseHttpRequest() {
            if (RawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
                try {
                    Url = new Uri(RawUrl);
                }
                catch { }
            }
        }

        /// <summary>
        /// Return the Full Qualified URL from request
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public static string FQURL(HttpRequest request) {
            return Uri.UnescapeDataString((request.Header("X-Forwarded-Proto") ?? "http") + "://" + (request.Header("X-Forwarded-Host") ?? request.Header("Host")) + request.Url);
        }

        /// <summary>
        /// Parse parameter/value in a routerHandleMatch
        /// </summary>
        /// <param name="routeHandleMatch"></param>
        /// <returns></returns>
        public object[] ParameterValues(Route routeHandleMatch) {
            GroupCollection matches = routeHandleMatch.regex.Match(Url.AbsolutePath).Groups;

            int i = 0;
            NameValueCollection qs = ParseQueryString(Url.Query);
            List<object> parameters = new();
            foreach (ParameterInfo handlerParameterInfo in routeHandleMatch.Handler.Parameters.Keys) {

                // if a parameterInfo name is found in request.url
                if (routeHandleMatch.parameters_name.Contains(handlerParameterInfo.Name)) {
                    // matches 0 is full regexp group so start from 1 for first matching brace
                    string value_string = UrlDecode(matches[routeHandleMatch.parameters_name.IndexOf(handlerParameterInfo.Name) + 1].Value);
                    object? value_type = ChangeType(value_string, handlerParameterInfo.ParameterType);
                    parameters.Add(value_type);
                }
                // if a parameterInfo name is found in query in request.url
                else if (routeHandleMatch.Attribute.QueryStringMappingEnabled
                         && qs[handlerParameterInfo.Name] != null
                ) {
                    string value_string = UrlDecode(qs[handlerParameterInfo.Name]);
                    object? value_type = ChangeType(value_string, handlerParameterInfo.ParameterType);
                    parameters.Add(value_type);
                }
                else {
                    // default value defined in parameterInfo Handler
                    object value = routeHandleMatch.Handler.Parameters[handlerParameterInfo];
                    parameters.Add(value);
                }

                i++;
            }

            return parameters.ToArray();
        }

        /// <summary>
        /// Custom Type Converter
        /// </summary>
        /// <param name="value_string">The string value to convert</param>
        /// <param name="type">The Type to value will be converted</param>
        /// <returns></returns>
        private static object? ChangeType(string value_string, Type type) {
            Type underlying_type = Nullable.GetUnderlyingType(type) ?? type;

            // Guid exception
            if (underlying_type == typeof(Guid)) {
                return Guid.Parse(value_string);
            }
            // DateOnly exception
            if (underlying_type == typeof(DateOnly)) {
                return DateOnly.Parse(value_string);
            }
            // use the global Convert
            object? value_type = (value_string == null) ? null : Convert.ChangeType(value_string, underlying_type);
            return value_type;
        }

        /// <summary>
        /// Parses the query string into the internal dictionary
        /// and optionally also returns this dictionary
        /// </summary>
        /// <param name="url">The string Url</param>
        /// <returns></returns>
        public static NameValueCollection ParseQueryString(string url) {
            NameValueCollection qs = new();

            if (string.IsNullOrWhiteSpace(url)) {
                return qs;
            }
            
            int index = url.IndexOf('?');
            if (index > -1) {
                if (url.Length >= index + 1) {
                    url = url[(index + 1)..];
                }
            }

            string[] pairs = url.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string pair in pairs) {
                int index2 = pair.IndexOf('=');
                if (index2 > 0) {
                    qs.Add(pair[..index2], pair[(index2 + 1)..]);
                }
            }

            return qs;
        }

        /// <summary>
        /// Decode string
        /// </summary>
        /// <param name="str">The string str</param>
        /// <returns></returns>
        private static string UrlDecode(string str) {
            return WebUtility.UrlDecode(str);
        }

        #endregion parse

    }

}
