using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Main HTTP/WebSocker Server Object
    /// </summary>
    public class SimpleWServer : WsServer {

        /// <summary>
        /// API/WEBSOCKET routes to handle
        /// </summary>
        
        public Router Router { get; private set; } = new Router();

        #region netcoreserver

        /// <summary>
        /// Herited Mandatory Constructor
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public SimpleWServer(IPAddress address, int port) : base(address, port) { }

        /// <summary>
        /// Herited mandatory factory http/ws session builder
        /// </summary>
        /// <returns></returns>
        protected override SimpleWSession CreateSession() { return new SimpleWSession(this); }

        /// <summary>
        /// Herited Optionnal Override SocketError
        /// </summary>
        /// <param name="error"></param>
        protected override void OnError(SocketError error) {
            var activity = source.StartActivity();
            if (activity != null) {
                activity.DisplayName = "Server OnError()";
                activity.SetStatus(ActivityStatusCode.Error);
                var tagsCollection = new ActivityTagsCollection {
                    { "exception.type", nameof(SocketError) },
                };
                activity.AddEvent(new ActivityEvent("exception", default, tagsCollection));
                activity.Stop();
            }
        }

        #endregion netcoreserver

        #region static

        /// <summary>
        /// File to get by default (default: "index.html")
        /// </summary>
        public string DefaultDocument = "index.html";

        /// <summary>
        /// Enable AutoIndex when DefaultDocument does not exists
        /// scope : global to all AddStaticContent()
        /// </summary>
        public bool AutoIndex { get; set; } = false;

        /// <summary>
        /// Add custom MimeTypes
        /// </summary>
        /// <param name="extension">The String extension</param>
        /// <param name="contentType">The String contentType</param>
        public void AddMimeTypes(string extension, string contentType) {
            HttpResponse.AddMimeTypes(extension, contentType);
        }

        #endregion static

        #region restapi

        /// <summary>
        /// List of all API controllers
        /// </summary>
        private readonly HashSet<Type> _controllers_api = new();

        /// <summary>
        /// Add dynamic content by registered all controllers which inherit from Controller
        /// </summary>
        /// <param name="path">path (default is "/")</param>
        /// <param name="excludes">List of Controller to not auto load</param>
        public void AddDynamicContent(string path = "/", IEnumerable<Type> excludes = null) {
            foreach (var controller in ControllerMethodExecutor.Controllers(excludes)) {
                AddDynamicContent(controller, path);
            }
        }

        /// <summary>
        /// Add dynamic content for a controller type which inherit from Controller
        /// </summary>
        /// <param name="controllerType">controllerType</param>
        /// <param name="path">path (default is "/")</param>
        public void AddDynamicContent(Type controllerType, string path = "/") {

            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentNullException(nameof(path));
            }

            if (_controllers_api.Contains(controllerType)) {
                throw new ArgumentException("Controller type is already registered in this module.", nameof(controllerType));
            }

            if (controllerType.IsAbstract
                || controllerType.IsGenericTypeDefinition
                || !controllerType.IsSubclassOf(typeof(Controller))
            ) {
                throw new ArgumentException($"Controller type must be a non-abstract subclass of {nameof(Controller)}.", nameof(controllerType));
            }

            var constructor = controllerType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0)
                              ?? throw new ArgumentException("Controller type must have a public parameterless constructor.", nameof(controllerType));

            path += controllerType.GetCustomAttributes()
                                    .OfType<RouteAttribute>()
                                    .Select(r => r.Path).DefaultIfEmpty("").FirstOrDefault();

            var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                        .Where(m => !m.ContainsGenericParameters);

            // all method with route attribute
            foreach (MethodInfo method in methods) {
                var attributes = method.GetCustomAttributes()
                                       .OfType<RouteAttribute>()
                                       .ToArray();

                if (attributes.Length < 1) {
                    continue;
                }

                // a method can have one or more route attribute
                foreach (var attribute in attributes) {
                    if (attribute.Method == "WEBSOCKET") {
                        continue;
                    }
                    attribute.SetPrefix(path);
                    var route = new Route(attribute, ControllerMethodExecutor.Create(method));
                    Router.AddRoute(route);
                }
            }

            _controllers_api.Add(controllerType);
        }

        /// <summary>
        /// Set Token settings and set delegate called by TokenWebUser to refresh webuser
        /// </summary>
        /// <param name="tokenPassphrase">The String token secret passphrase (min 17 chars).</param>
        /// <param name="issuer">The String issuer.</param>
        /// <param name="tokenWebUserCallback">The DelegateSetTokenWebUser setTokenWebUser</param>
        public void SetToken(string tokenPassphrase, string issuer, DelegateSetTokenWebUser tokenWebUserCallback = null) {

            if (string.IsNullOrWhiteSpace(tokenPassphrase) || tokenPassphrase.Length <= 16) {
                throw new ArgumentException($"{nameof(tokenPassphrase)} must be 17 char length minimum");
            }
            Controller.TokenKey = tokenPassphrase;

            Controller.TokenIssuer = issuer;
            
            if (tokenWebUserCallback != null) {
                Controller.GetWebUserCallback = tokenWebUserCallback;
            }
        }

        #endregion restapi

        #region websocket

        /// <summary>
        /// List of all Websocket controllers
        /// </summary>
        private readonly HashSet<Type> _controllers_websocket = new();

        /// <summary>
        /// Prefixes of websocket route
        /// </summary>
        public readonly HashSet<string> _websocket_prefix_routes = new();

        /// <summary>
        /// Add WEBSOCKET controller content by registered all controllers which inherit from Controller
        /// </summary>
        /// <param name="path">path (default is "/websocket")</param>
        /// <param name="excepts">List of Controller to not auto load</param>
        public void AddWebSocketContent(string path = "/websocket", IEnumerable<Type> excepts = null) {

            // case when no controller is defined, we need to store the prefix
            // for socket the handshake complete (see SimpleWSession.OnReceivedRequest() return).
            if (!_websocket_prefix_routes.Contains(path)) {
                _websocket_prefix_routes.Add(path);
            }

            foreach (var controller in ControllerMethodExecutor.Controllers(excepts)) {
                AddWebSocketContent(controller, path);
            }
        }

        /// <summary>
        /// Add WEBSOCKET controller content for a controller type which inherit from Controller
        /// </summary>
        /// <param name="controllerType">controllerType</param>
        /// <param name="path">path (default is "/websocket")</param>
        public void AddWebSocketContent(Type controllerType, string path = "/websocket") {

            if (!_websocket_prefix_routes.Contains(path)) {
                _websocket_prefix_routes.Add(path);
            }

            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentNullException(nameof(path));
            }

            if (_controllers_websocket.Contains(controllerType)) {
                throw new ArgumentException("Controller type is already registered in this module.", nameof(controllerType));
            }

            if (controllerType.IsAbstract
                || controllerType.IsGenericTypeDefinition
                || !controllerType.IsSubclassOf(typeof(Controller))
            ) {
                throw new ArgumentException($"Controller type must be a non-abstract subclass of {nameof(Controller)}.", nameof(controllerType));
            }

            var constructor = controllerType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0)
                              ?? throw new ArgumentException("Controller type must have a public parameterless constructor.", nameof(controllerType));

            path += controllerType.GetCustomAttributes()
                                    .OfType<RouteAttribute>()
                                    .Select(r => r.Path).DefaultIfEmpty("").FirstOrDefault();

            var methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                        .Where(m => !m.ContainsGenericParameters);

            // all method with route attribute
            foreach (MethodInfo method in methods) {
                var attributes = method.GetCustomAttributes()
                                       .OfType<RouteAttribute>()
                                       .ToArray();

                if (attributes.Length < 1) {
                    continue;
                }

                // a method can have one or more route attribute
                foreach (var attribute in attributes) {
                    if (attribute.Method != "WEBSOCKET") {
                        continue;
                    }
                    attribute.SetPrefix(path);
                    var route = new Route(attribute, ControllerMethodExecutor.Create(method));
                    Router.AddRoute(route);
                }
            }

            _controllers_websocket.Add(controllerType);
        }

        /// <summary>
        /// WebUsers sessions
        /// </summary>
        public readonly ConcurrentDictionary<Guid, IWebUser> WebUsers = new();

        /// <summary>
        /// Return All wsSession based on func
        /// </summary>
        /// <param name="where">filter expression</param>
        /// <returns></returns>
        public IEnumerable<WsSession> AllWebUsers(Func<KeyValuePair<Guid, IWebUser>, bool> where) {
            foreach (var wu in WebUsers.Where(where)) {
                var session = FindSession(wu.Key);
                if (session is WsSession wsSession) {
                    yield return wsSession;
                }
            }
        }

        /// <summary>
        /// Find a webuser with a given Id
        /// </summary>
        /// <param name="id">Session Id</param>
        /// <returns>Session with a given Id or null if the webuser it not connected</returns>
        public IWebUser FindWebUser(Guid id) {
            return WebUsers.TryGetValue(id, out IWebUser result) ? result : null;
        }

        /// <summary>
        /// Register a new webuser
        /// </summary>
        /// <param name="id"></param>
        /// <param name="webuser">webuser to register</param>
        public void RegisterWebUser(Guid id, IWebUser webuser) {
            WebUsers.TryAdd(id, webuser);
        }

        /// <summary>
        /// Unregister webuser by Id
        /// </summary>
        /// <param name="id">Session Id</param>
        public void UnregisterWebUser(Guid id) {
            WebUsers.TryRemove(id, out IWebUser _);
        }

        #endregion websocket

        #region cors

        /// <summary>
        /// CORS Header Origin
        /// </summary>
        public string cors_allow_origin;
        /// <summary>
        /// CORS Header headers
        /// </summary>
        public string cors_allow_headers;
        /// <summary>
        /// CORS Header methods
        /// </summary>
        public string cors_allow_methods;
        /// <summary>
        /// CORS Header credentials
        /// </summary>
        public string cors_allow_credentials;

        /// <summary>
        /// Setup CORS
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="headers"></param>
        /// <param name="methods"></param>
        /// <param name="credentials"></param>
        public void AddCORS(string origin="*", string headers = "*", string methods = "GET,POST,OPTIONS", string credentials="true") {
            this.cors_allow_origin = origin;
            this.cors_allow_headers = headers;
            this.cors_allow_methods = methods;
            this.cors_allow_credentials = credentials;
        }

        #endregion cors

        #region OpenTelemetry

        /// <summary>
        /// ActivitySource
        /// </summary>
        protected static ActivitySource source = new ActivitySource("SimpleW", Assembly.GetExecutingAssembly().GetName().Version.ToString());

        #endregion OpenTelemetry

    }

}
