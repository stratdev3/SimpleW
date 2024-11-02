using System;
using System.Collections.Generic;
using System.Linq;


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
        /// Public Property List of all declared and handle Routes
        /// </summary>
        public List<Route> Routes {
            get {
                if (RegExpEnabled) {
                    return _routes_list;
                }
                else {
                    return _routes_dict.Select(d => d.Value).ToList();
                }
            }
        }

        /// <summary>
        /// Add route to the Routes property
        /// Check if Path contains regular expression and set RegExpEnabled to true if so
        /// </summary>
        /// <param name="route"></param>
        public void AddRoute(Route route) {
            if (RegExpEnabled) {
                _routes_list.Add(route);
            }
            else {
                _routes_dict.Add(route.Method.ToUpper()+route.Attribute.Path, route);
            }
        }

        /// <summary>
        /// Return the route in Routes which match requestRoute
        /// </summary>
        /// <param name="requestRoute"></param>
        /// <returns></returns>
        public Route Match(Route requestRoute) {
            if (RegExpEnabled) {
                return _routes_list.FirstOrDefault(r => string.Equals(r.Method, requestRoute.Method, StringComparison.OrdinalIgnoreCase)
                                                        && r.regex.Match(requestRoute.Url.AbsolutePath).Success);
            }
            _routes_dict.TryGetValue(requestRoute.Method.ToUpper() + requestRoute.Url.AbsolutePath, out Route found_dict);
            return found_dict;
        }

        /// <summary>
        /// Return the route in Routes which match message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Route Match(WebSocketMessage message) {
            _routes_dict.TryGetValue("WEBSOCKET" + message.url, out Route found_dict);
            return found_dict;
        }

    }

}
