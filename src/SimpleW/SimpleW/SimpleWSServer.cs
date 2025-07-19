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
    /// Main HTTPS/SecureWebSocket Server Object
    /// </summary>
    public class SimpleWSServer : WssServer, ISimpleWServer {

        /// <summary>
        /// API/WEBSOCKET routes to handle
        /// </summary>
        
        public Router Router { get; private set; } = new Router();

        #region netcoreserver

        /// <summary>
        /// Herited Mandatory Constructor
        /// </summary>
        /// <param name="context"></param>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public SimpleWSServer(SslContext context, IPAddress address, int port) : base(context, address, port) { }

        /// <summary>
        /// Herited mandatory factory https/wss session builder
        /// </summary>
        /// <returns></returns>
        protected override SslSession CreateSession() { return new SimpleWSSession(this); }

        /// <summary>
        /// Herited Optionnal Override SocketError
        /// </summary>
        /// <param name="error"></param>
        protected override void OnError(SocketError error) {
            Activity? activity = source.StartActivity();
            if (activity != null) {
                activity.DisplayName = "Server OnError()";
                activity.SetStatus(ActivityStatusCode.Error);
                ActivityTagsCollection tagsCollection = new() {
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
        public string DefaultDocument { get; set; } = "index.html";

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

        #region dynamic

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
            foreach (Type controller in ControllerMethodExecutor.Controllers(excludes)) {
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

            ConstructorInfo constructor = controllerType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0)
                                          ??
                                          throw new ArgumentException("Controller type must have a public parameterless constructor.", nameof(controllerType));

            path += controllerType.GetCustomAttributes()
                                    .OfType<RouteAttribute>()
                                    .Select(r => r.Path).DefaultIfEmpty("").FirstOrDefault();

            IEnumerable<MethodInfo> methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                                            .Where(m => !m.ContainsGenericParameters);

            // all method with route attribute
            foreach (MethodInfo method in methods) {
                RouteAttribute[] attributes = method.GetCustomAttributes()
                                                    .OfType<RouteAttribute>()
                                                    .ToArray();

                if (attributes.Length < 1) {
                    continue;
                }

                // a method can have one or more route attribute
                foreach (RouteAttribute attribute in attributes) {
                    if (attribute.Method == "WEBSOCKET") {
                        continue;
                    }
                    attribute.SetPrefix(path);
                    Route route = new(attribute, ControllerMethodExecutor.Create(method));
                    Router.AddRoute(route);
                }
            }

            _controllers_api.Add(controllerType);
        }

        /// <summary>
        /// Set Token settings (passphrase and issuer).
        /// a delegate can be defined to redress webuser called by Controller.JwtToWebUser().
        /// </summary>
        /// <param name="tokenPassphrase">The String token secret passphrase (min 17 chars).</param>
        /// <param name="issuer">The String issuer.</param>
        /// <param name="getWebUserCallback">The DelegateSetTokenWebUser getWebUserCallback</param>
        public void SetToken(string tokenPassphrase, string issuer, DelegateSetTokenWebUser getWebUserCallback = null) {

            if (string.IsNullOrWhiteSpace(tokenPassphrase) || tokenPassphrase.Length <= 16) {
                throw new ArgumentException($"{nameof(tokenPassphrase)} must be 17 char length minimum");
            }
            Controller.TokenKey = tokenPassphrase;

            Controller.TokenIssuer = issuer;
            
            if (getWebUserCallback != null) {
                Controller.GetWebUserCallback = getWebUserCallback;
            }
        }

        #endregion dynamic

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

            foreach (Type controller in ControllerMethodExecutor.Controllers(excepts)) {
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

            ConstructorInfo constructor = controllerType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 0)
                                          ??
                                          throw new ArgumentException("Controller type must have a public parameterless constructor.", nameof(controllerType));

            path += controllerType.GetCustomAttributes()
                                    .OfType<RouteAttribute>()
                                    .Select(r => r.Path).DefaultIfEmpty("").FirstOrDefault();

            IEnumerable<MethodInfo> methods = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                                            .Where(m => !m.ContainsGenericParameters);

            // all method with route attribute
            foreach (MethodInfo method in methods) {
                RouteAttribute[] attributes = method.GetCustomAttributes()
                                                    .OfType<RouteAttribute>()
                                                    .ToArray();

                if (attributes.Length < 1) {
                    continue;
                }

                // a method can have one or more route attribute
                foreach (RouteAttribute attribute in attributes) {
                    if (attribute.Method != "WEBSOCKET") {
                        continue;
                    }
                    attribute.SetPrefix(path);
                    Route route = new(attribute, ControllerMethodExecutor.Create(method));
                    Router.AddRoute(route);
                }
            }

            _controllers_websocket.Add(controllerType);
        }

        /// <summary>
        /// WebUsers sessions
        /// </summary>
        public ConcurrentDictionary<Guid, IWebUser> WebSocketUsers { get; private set; } = new();

        /// <summary>
        /// Return All wsSession based on func
        /// </summary>
        /// <param name="where">filter expression</param>
        /// <returns></returns>
        public IEnumerable<IWebSocketSession> AllWebSocketUsers(Func<KeyValuePair<Guid, IWebUser>, bool> where) {
            foreach (KeyValuePair<Guid, IWebUser> wu in WebSocketUsers.Where(where)) {
                SslSession session = FindSession(wu.Key);
                if (session is WssSession wsSession) {
                    yield return wsSession;
                }
            }
        }

        /// <summary>
        /// Find a webuser with a given Id
        /// </summary>
        /// <param name="id">Session Id</param>
        /// <returns>Session with a given Id or null if the webuser it not connected</returns>
        public IWebUser FindWebSocketUser(Guid id) {
            return WebSocketUsers.TryGetValue(id, out IWebUser result) ? result : null;
        }

        /// <summary>
        /// Register a new webuser
        /// </summary>
        /// <param name="id"></param>
        /// <param name="webuser">webuser to register</param>
        public void RegisterWebSocketUser(Guid id, IWebUser webuser) {
            WebSocketUsers.TryAdd(id, webuser);
        }

        /// <summary>
        /// Unregister webuser by Id
        /// </summary>
        /// <param name="id">Session Id</param>
        public void UnregisterWebSocketUser(Guid id) {
            WebSocketUsers.TryRemove(id, out IWebUser _);
        }

        #endregion websocket

        #region cors

        /// <summary>
        /// CORS Header Origin
        /// </summary>
        public string cors_allow_origin {
            get {
                return Controller.cors_allow_origin;
            }
            set {
                Controller.cors_allow_origin = value;
            }
        }

        /// <summary>
        /// CORS Header headers
        /// </summary>
        public string cors_allow_headers {
            get {
                return Controller.cors_allow_headers;
            }
            set {
                Controller.cors_allow_headers = value;
            }
        }

        /// <summary>
        /// CORS Header methods
        /// </summary>
        public string cors_allow_methods {
            get {
                return Controller.cors_allow_methods;
            }
            set {
                Controller.cors_allow_methods = value;
            }
        }

        /// <summary>
        /// CORS Header credentials
        /// </summary>
        public string cors_allow_credentials {
            get {
                return Controller.cors_allow_credentials;
            }
            set {
                Controller.cors_allow_credentials = value;
            }
        }

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
        protected static ActivitySource source = new("SimpleW", Assembly.GetExecutingAssembly().GetName().Version.ToString());

        #endregion OpenTelemetry

    }

}
