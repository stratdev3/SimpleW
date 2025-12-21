using System;
using System.Collections.Generic;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Router class
    /// </summary>
    public class Router {

        /// <summary>
        /// Enable Regular Expression for Route.Path
        /// Consider RegExpEnabled to be slower
        /// scope : global to all AddDynamicContent()
        /// </summary>
        public bool RegExpEnabled { get; set; } = false;

        /// <summary>
        /// Dictionnary of all declared and handle Routes
        /// Fill when RegExpEnabled = false
        /// </summary>
        private readonly Dictionary<string, Route> _routes_dict = new();

        /// <summary>
        /// List of all declared and handle Routes
        /// Fill when RegExpEnabled = true
        /// </summary>
        private readonly List<Route> _routes_list = new();

        /// <summary>
        /// All declared Routes
        /// </summary>
        public IEnumerable<Route> Routes => RegExpEnabled ? _routes_list : _routes_dict.Values;

        /// <summary>
        /// Add route to the Routes property
        /// </summary>
        /// <param name="routeAttribute"></param>
        /// <param name="handler"></param>
        public void AddRoute(RouteAttribute routeAttribute, ControllerMethodExecutor handler) {
            Route route = new(routeAttribute, handler, RegExpEnabled);

            if (RegExpEnabled) {
                _routes_list.Add(route);
            }
            else {
                _routes_dict.Add(route.Attribute.Method.ToUpper() + route.Attribute.Path, route);
            }
        }

        /// <summary>
        /// Clears all Routes
        /// </summary>
        public void ClearRoutes() {
            _routes_list.Clear();
            _routes_dict.Clear();
        }

        /// <summary>
        /// Find a route in Routes dependings on HttpRequest
        /// </summary>
        /// <param name="request">The HttpRequest request</param>
        /// <returns></returns>
        public Route Find(HttpRequest request) {
            if (RegExpEnabled) {
                foreach (Route route in _routes_list) {
                    if (!string.Equals(route.Attribute.Method, request.Method, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    if (route.regex.IsMatch(request.Uri.AbsolutePath)) {
                        return route;
                    }
                }
                return null;
            }
            else {
                _routes_dict.TryGetValue(request.Method.ToUpper() + request.Uri.AbsolutePath, out Route found_dict);
                return found_dict;
            }
        }

        /// <summary>
        /// Find a route in Routes dependings on WebSocketMessage
        /// </summary>
        /// <param name="message">the WebSocketMessage message</param>
        /// <returns></returns>
        public Route Find(WebSocketMessage message) {
            _routes_dict.TryGetValue("WEBSOCKET" + message.url, out Route found_dict);
            return found_dict;
        }

    }

}
