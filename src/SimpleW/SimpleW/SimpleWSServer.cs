using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Main HTTPS/SecureWebSocket Server Object
    /// </summary>
    public class SimpleWSServer : WssServer, ISimpleWServer {

        /// <summary>
        /// Main Router instance
        /// </summary>
        public Router Router { get; private set; } = new Router();

        #region options

        /// <summary>
        /// Json Serializer/Deserializer
        /// </summary>
        private IJsonEngine _jsonEngine = new SystemTextJsonEngine(SystemTextJsonEngine.OptionsSimpleWBuilder());
        public IJsonEngine JsonEngine {
            get => _jsonEngine;
            set {
                _jsonEngine = value;
                NetCoreServerExtension.JsonEngine = value;
            }
        }

        #endregion options

        #region security

        /// <summary>
        /// True to allow some headers as source of truth for Telemetry
        /// Example : X-Forwarded-Host, X-Real-IP (...) are often used to pass data
        ///           from reverse proxy (nginx...) to upstream server.
        /// Note : you should allow only if you have a reverse proxy with well defined settings policy
        /// </summary>
        public bool TrustXHeaders { get; set; } = false;

        #endregion security

        #region netcoreserver

        /// <summary>
        /// Initialize server with a given IP address and port number
        /// </summary>
        /// <param name="context">SSL context</param>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        public SimpleWSServer(SslContext context, IPAddress address, int port) : base(context, address, port) { }

        /// <summary>
        /// Initialize server with a given IP address and port number
        /// </summary>
        /// <param name="context">SSL context</param>
        /// <param name="address">IP address</param>
        /// <param name="port">Port number</param>
        public SimpleWSServer(SslContext context, string address, int port) : base(context, address, port) { }

        /// <summary>
        /// Initialize server with a given DNS endpoint
        /// </summary>
        /// <param name="context">SSL context</param>
        /// <param name="endpoint">DNS endpoint</param>
        public SimpleWSServer(SslContext context, DnsEndPoint endpoint) : base(context, endpoint) { }

        /// <summary>
        /// Initialize server with a given IP endpoint
        /// </summary>
        /// <param name="context">SSL context</param>
        /// <param name="endpoint">IP endpoint</param>
        public SimpleWSServer(SslContext context, IPEndPoint endpoint) : base(context, endpoint) { }

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
            if (EnableTelemetry) {
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
        }

        #endregion netcoreserver

        #region staticContent

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
        /// List of all static content
        /// </summary>
        private readonly Dictionary<string, (string, string, string, bool)> _staticContents = new();

        /// <summary>
        /// Add custom MimeTypes
        /// </summary>
        /// <param name="extension">The String extension</param>
        /// <param name="contentType">The String contentType</param>
        public void AddMimeTypes(string extension, string contentType) {
            HttpResponse.AddMimeTypes(extension, contentType);
        }

        /// <summary>
        /// Add static content
        /// The timeout parameter control how long the content is cached (null or 0 mean no cache at all)
        /// When cache, there is an underlying file watcher to refresh cache on file change
        /// </summary>
        /// <param name="path">Static content path</param>
        /// <param name="prefix">Cache prefix (default is "/")</param>
        /// <param name="filter">Cache filter (default is "*.*")</param>
        /// <param name="timeout">Refresh cache timeout (0 or null mean no cache, default: null)</param>
        public new void AddStaticContent(string path, string prefix = "/", string filter = "*.*", TimeSpan? timeout = null) {
            if (string.IsNullOrWhiteSpace(prefix)) {
                throw new ArgumentNullException(nameof(prefix));
            }

            // no cache, read from disk
            if (timeout == null) {
                _staticContents.Add(prefix, (path, prefix, filter, false));
            }
            // cache and file watcher
            else {
                _staticContents.Add(prefix, (path, prefix, filter, true));
                base.AddStaticContent(path, prefix, filter, timeout);
            }
        }

        /// <summary>
        /// Remove static content cache
        /// </summary>
        /// <param name="path">Static content path</param>
        public new void RemoveStaticContent(string path) {
            _staticContents.Remove(path);
            base.RemoveStaticContent(path);
        }

        /// <summary>
        /// Clear static content cache
        /// </summary>
        public new void ClearStaticContent() {
            _staticContents.Clear();
            base.ClearStaticContent();
        }

        /// <summary>
        /// True url can contains static content
        /// </summary>
        /// <param name="url"></param>
        /// <param name="prefix"></param>
        /// <param name="path"></param>
        /// <param name="filter"></param>
        /// <param name="cached"></param>
        /// <returns></returns>
        public bool HasStaticContent(string url, out string path, out string prefix, out string filter, out bool cached) {
            path = null;
            prefix = null;
            filter = null;
            cached = false;
            foreach (KeyValuePair<string, (string, string, string, bool)> staticContent in _staticContents) {
                if (url.StartsWith(staticContent.Key)) {
                    (path, prefix, filter, cached) = staticContent.Value;
                    return true;
                }
            }
            return false;
        }

        #endregion staticContent

        #region func

        /// <summary>
        /// Add Func content for GET request
        /// Available arguments :
        ///     - ISimpleWSession session
        ///     - HttpRequest request
        ///     - any query string name
        /// </summary>
        /// <param name="url"></param>
        /// <param name="handler"></param>
        public void MapGet(string url, Delegate handler) {
            AddFunc("GET", url, handler);
        }

        /// <summary>
        /// Add Func content for POST request
        /// Available arguments :
        ///     - ISimpleWSession session
        ///     - HttpRequest request
        ///     - any query string name
        /// </summary>
        /// <param name="url"></param>
        /// <param name="handler"></param>
        public void MapPost(string url, Delegate handler) {
            AddFunc("POST", url, handler);
        }

        /// <summary>
        /// Add Func content
        /// Available arguments :
        ///     - ISimpleWSession session
        ///     - HttpRequest request
        ///     - any query string name
        /// </summary>
        /// <param name="verb">GET, POST...</param>
        /// <param name="url"></param>
        /// <param name="handler"></param>
        private void AddFunc(string verb, string url, Delegate handler) {
            if (IsStarted) {
                throw new InvalidOperationException("AddFunc content cannot be added when server is already started");
            }
            Router.AddRoute(new RouteAttribute(verb, url, isAbsolutePath: true), ControllerMethodExecutor.Create(handler));
        }

        #endregion func

        #region dynamicContent

        /// <summary>
        /// List of all dynamic content
        /// </summary>
        private readonly HashSet<Type> _dynamicContents = new();

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

            if (IsStarted) {
                throw new InvalidOperationException("Dynamic content cannot be added when server is already started");
            }

            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentNullException(nameof(path));
            }

            if (_dynamicContents.Contains(controllerType)) {
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
                    Router.AddRoute(attribute, ControllerMethodExecutor.Create(method));
                }
            }

            _dynamicContents.Add(controllerType);
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

        #endregion dynamicContent

        #region sse

        /// <summary>
        /// Server Sent Events sessions
        /// </summary>
        ConcurrentBag<ISimpleWSession> SSESessions = new ConcurrentBag<ISimpleWSession>();

        /// <summary>
        /// Add session to the list of SSESession
        /// </summary>
        /// <param name="session"></param>
        public void AddSSESession(ISimpleWSession session) {
            SSESessions.Add(session);
        }

        /// <summary>
        /// Remove session to the list of SSESession
        /// </summary>
        /// <param name="session"></param>
        public void RemoveSSESession(ISimpleWSession session) {
            SSESessions.TryTake(out ISimpleWSession sessions);
        }

        /// <summary>
        /// Send data conformed to Server Sent Event to filtered SSE Sessions
        /// </summary>
        /// <param name="evt">the event name</param>
        /// <param name="data">the data</param>
        /// <param name="filter">filter the SSESessions (default: null)</param>
        public void BroadcastSSESessions(string evt, string data, Expression<Func<ISimpleWSession, bool>> filter = null) {
            string payload = $"event: {evt}\n\n" +
                             $"data: {data}\n\n";
            byte[] bytes = Encoding.UTF8.GetBytes(payload);

            if (filter == null) {
                foreach (ISimpleWSession session in SSESessions) {
                    try {
                        ((SslSession)session).SendAsync(bytes);
                    }
                    catch {
                        RemoveSSESession(session);
                    }
                }
            }
            else {
                foreach (ISimpleWSession session in SSESessions.AsQueryable().Where(filter)) {
                    try {
                        ((SslSession)session).SendAsync(bytes);
                    }
                    catch {
                        RemoveSSESession(session);
                    }
                }
            }
        }

        #endregion sse

        #region websocketContent

        /// <summary>
        /// Is WebSocket Enabled
        /// </summary>
        public bool IsWebSocketEnabled { get; private set; } = false;

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

            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentNullException(nameof(path));
            }

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

            if (IsStarted) {
                throw new InvalidOperationException("Dynamic content cannot be added when server is already started");
            }

            if (string.IsNullOrWhiteSpace(path)) {
                throw new ArgumentNullException(nameof(path));
            }

            if (!_websocket_prefix_routes.Contains(path)) {
                _websocket_prefix_routes.Add(path);
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
                    Router.AddRoute(attribute, ControllerMethodExecutor.Create(method));
                }
            }

            _controllers_websocket.Add(controllerType);
            IsWebSocketEnabled = true;
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

        #endregion websocketContent

        #region cors

        /// <summary>
        /// CORS Header Origin
        /// </summary>
        public string cors_allow_origin {
            get {
                return HttpResponse.cors_allow_origin;
            }
            set {
                HttpResponse.cors_allow_origin = value;
            }
        }

        /// <summary>
        /// CORS Header headers
        /// </summary>
        public string cors_allow_headers {
            get {
                return HttpResponse.cors_allow_headers;
            }
            set {
                HttpResponse.cors_allow_headers = value;
            }
        }

        /// <summary>
        /// CORS Header methods
        /// </summary>
        public string cors_allow_methods {
            get {
                return HttpResponse.cors_allow_methods;
            }
            set {
                HttpResponse.cors_allow_methods = value;
            }
        }

        /// <summary>
        /// CORS Header credentials
        /// </summary>
        public string cors_allow_credentials {
            get {
                return HttpResponse.cors_allow_credentials;
            }
            set {
                HttpResponse.cors_allow_credentials = value;
            }
        }

        /// <summary>
        /// Setup CORS
        /// </summary>
        /// <param name="origin">Access-Control-Allow-Origin</param>
        /// <param name="headers">Access-Control-Allow-Headers</param>
        /// <param name="methods">Access-Control-Allow-Methods</param>
        /// <param name="credentials">Access-Control-Allow-Credentials</param>
        public void AddCORS(string origin="*", string headers = "*", string methods = "GET,POST,OPTIONS", string credentials="true") {
            this.cors_allow_origin = origin;
            this.cors_allow_headers = headers;
            this.cors_allow_methods = methods;
            this.cors_allow_credentials = credentials;
        }

        #endregion cors

        #region OpenTelemetry

        /// <summary>
        /// True to generate Telemetry Traces, Logs and Metrics
        /// </summary>
        public bool EnableTelemetry { get; set; } = false;

        /// <summary>
        /// ActivitySource
        /// </summary>
        protected static ActivitySource source = new("SimpleW", Assembly.GetExecutingAssembly().GetName().Version.ToString());

        #endregion OpenTelemetry

    }

}
