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
        /// </summary>
        public bool RegExpEnabled { get; set; } = false;

        /// <summary>
        /// List of all declare and handle Routes
        /// </summary>
        private List<Route> _routes = new();
        public List<Route> Routes { 
            get {
                return _routes;
            }
        }

        /// <summary>
        /// Add route to the Routes property
        /// Check if Path contains regular expression and set RegExpEnabled to true if so
        /// </summary>
        /// <param name="route"></param>
        public void AddRoute(Route route) {
            // detect if Path contains RegExp
            if (!RegExpEnabled && (new[] { "^", "$", ".", "*", "{", "(" }).Any(route.Attribute.Path.Contains)) {
                RegExpEnabled = true;
            }
            _routes.Add(route);
        }

        /// <summary>
        /// Return the route in Routes which match requestRoute
        /// </summary>
        /// <param name="requestRoute"></param>
        /// <returns></returns>
        public Route Match(Route requestRoute) {
            if (RegExpEnabled) {
                return _routes.FirstOrDefault(r => string.Equals(r.Method, requestRoute.Method, StringComparison.OrdinalIgnoreCase)
                                                   && r.regex.Match(requestRoute.Url.AbsolutePath).Success);
            }
            else {
                return _routes.FirstOrDefault(r => string.Equals(r.Method, requestRoute.Method, StringComparison.OrdinalIgnoreCase)
                                                   && string.Equals(r.Attribute.Path, requestRoute.Url.AbsolutePath));
            }
        }

        /// <summary>
        /// Return the route in Routes which match message
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public Route Match(WebSocketMessage message) {
            return _routes.FirstOrDefault(r => string.Equals(r.Method, "WEBSOCKET", StringComparison.OrdinalIgnoreCase)
                                               && string.Equals(r.RawUrl, message.url));
        }

    }

}
