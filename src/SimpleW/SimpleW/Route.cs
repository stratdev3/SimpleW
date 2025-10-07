using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Route class
    /// </summary>
    public partial class Route {

        /// <summary>
        /// The RouteAttribute
        /// </summary>
        public readonly RouteAttribute Attribute;

        /// <summary>
        /// The ControllerMethodExecutor
        /// </summary>
        public ControllerMethodExecutor Handler { get; internal set; }

        /// <summary>
        /// Create Route from RouteAttribute
        /// </summary>
        /// <param name="attribute">The RouteAttribute attribute.</param>
        /// <param name="handler">The ControllerMethodExecutor handler.</param>
        /// <param name="regExpEnabled"></param>
        public Route(RouteAttribute attribute, ControllerMethodExecutor handler, bool regExpEnabled = false) {
            Attribute = attribute;
            Handler = handler;
            if (regExpEnabled) {
                ParsePathParameters();
            }
        }

        #region parameters

        private readonly List<string> parameters_name = new();

        /// <summary>
        /// The Regex use to parse parameter depending on current RouteAttribute
        /// </summary>
        public Regex regex { get; private set; }

        /// <summary>
        /// Build time Regex to look for parameter in routePath
        /// </summary>
        /// <returns></returns>
        [GeneratedRegex("{([^}]+)}")]
        private static partial Regex ParameterRegex();

        /// <summary>
        /// Parse Path Parameters
        /// </summary>
        private void ParsePathParameters() {

            // path
            string path = Uri.UnescapeDataString(Attribute.Path);
            int pos = path.IndexOf("?");
            string relativePath = pos > 0 ? path[..pos] : path;

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
        /// Parse parameter/value from an HttpRequest
        /// </summary>
        /// <param name="request">The HttpRequest to retrieve parameter</param>
        /// <returns></returns>
        public object[] ParameterValues(HttpRequest request) {

            GroupCollection matches = (regex != null) ? regex.Match(request.Uri.AbsolutePath).Groups : null;

            int i = 0;
            NameValueCollection qs = NetCoreServerExtension.ParseQueryString(request.Uri.Query);
            List<object> parameters = new();
            foreach (ParameterInfo handlerParameterInfo in Handler.Parameters.Keys) {

                // if regExpEnabled and a parameterInfo name is found in request.url
                if (regex != null && parameters_name.Contains(handlerParameterInfo.Name)) {
                    // matches 0 is full regexp group so start from 1 for first matching brace
                    string value_string = NetCoreServerExtension.UrlDecode(matches[parameters_name.IndexOf(handlerParameterInfo.Name) + 1].Value);
                    object? value_type = ChangeType(value_string, handlerParameterInfo.ParameterType);
                    parameters.Add(value_type);
                }
                // if a parameterInfo name is found in query in request.url
                else if (Attribute.QueryStringMappingEnabled
                         && qs[handlerParameterInfo.Name] != null
                ) {
                    string value_string = NetCoreServerExtension.UrlDecode(qs[handlerParameterInfo.Name]);
                    object? value_type = ChangeType(value_string, handlerParameterInfo.ParameterType);
                    parameters.Add(value_type);
                }
                else {
                    // default value defined in parameterInfo Handler
                    object value = Handler.Parameters[handlerParameterInfo];
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

        #endregion parameters

    }

}
