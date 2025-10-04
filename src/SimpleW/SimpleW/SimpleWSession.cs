using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Main http/ws session object
    /// </summary>
    public class SimpleWSession : WsSession, ISimpleWSession {

        #region properties

        /// <summary>
        /// override the inherited Server Instance
        /// </summary>
        public new ISimpleWServer Server => server;

        /// <summary>
        /// SimpleWServer Instance
        /// </summary>
        private readonly SimpleWServer server;

        /// <summary>
        /// JWT
        /// </summary>
        public string jwt { get; set; }

        /// <summary>
        /// <para>Get Current IWebUser</para>
        /// <para>set by the underlying Controller.webuser
        ///       The only use case to have a webuser
        ///       property here is for logging</para>
        /// </summary>
        public IWebUser webuser { get; set; }

        #endregion properties

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="server"></param>
        public SimpleWSession(SimpleWServer server) : base(server) {
            this.server = server;
        }

        #region http

        /// <summary>
        /// Request :
        ///     - http static (first access so not in cache)
        ///     - http options (preflight cors)
        ///     - http dynamic (inline func, controller)
        ///     - http upgrade to websocket (first access)
        ///     - http sse
        /// </summary>
        /// <param name="request"></param>
        protected override void OnReceivedRequest(HttpRequest request) {
            Activity? activity = CreateActivity();
            try {

                // parse : request.url to route
                Route requestRoute = new(request, Server.TrustXHeaders);
                SetDefaultActivity(activity, Request, requestRoute, Id, Socket, server.TrustXHeaders);

                // default document
                if (requestRoute.Method == "GET" && requestRoute.hasEndingSlash) {
                    (bool find, byte[] content) = Cache.Find(requestRoute.Url.AbsolutePath + server.DefaultDocument);
                    if (find) {
                        SendAsync(content);
                        StopWithStatusCodeActivity(activity, 200, content.Length);
                        return;
                    }
                    // autoindex
                    else if (server.AutoIndex) {
                        OnReceivedAutoIndexRequest(requestRoute);
                        StopWithStatusCodeActivity(activity, 200);
                        return;
                    }
                }

                // websocket force here to return cause no matching will return 404 and browser will close websocket connection
                if (server._websocket_prefix_routes.Contains(requestRoute.Url.AbsolutePath)) {
                    return;
                }

                // preflight request when client is requesting server CORS configuration
                if (requestRoute.Method == "OPTIONS"
                    && !string.IsNullOrWhiteSpace(server.cors_allow_origin)
                ) {
                    SendResponseAsync(Response.MakeCORSResponse(server.cors_allow_origin, server.cors_allow_headers, server.cors_allow_methods, server.cors_allow_credentials));
                    StopWithStatusCodeActivity(activity, Response, webuser);
                    return;
                }

                // get first matching route
                Route routeMatch = server.Router.Match(requestRoute);
                if (routeMatch != null) {
                    if (routeMatch.Handler != null) {
                        if (routeMatch.Handler.ExecuteFunc != null) {
                            object result = routeMatch.Handler.ExecuteFunc(this, request, requestRoute.ParameterValues(routeMatch));
                            if (result is HttpResponse response) {
                                SendResponseAsync(response);
                            }
                            else {
                                Response.MakeResponse(this.server.JsonEngine.Serialize(result), compress: Request.AcceptEncodings());
                                SendResponseAsync(Response);
                            }
                        }
                        else {
                            routeMatch.Handler.ExecuteMethod(this, request, requestRoute.ParameterValues(routeMatch));
                        }
                        StopWithStatusCodeActivity(activity, Response, webuser);
                        return;
                    }
                }

                SendResponseAsync(Response.MakeErrorResponse(404, "not found"));
                StopWithStatusCodeActivity(activity, Response, webuser);
            }
            catch (Exception ex) {
                SendResponseAsync(Response.MakeErrorResponse(500, "server error"));
                StopWithStatusCodeActivity(activity, Response, webuser, ex);
            }
        }

        /// <summary>
        /// Request for AutoIndex
        /// </summary>
        /// <param name="requestRoute"></param>
        protected void OnReceivedAutoIndexRequest(Route requestRoute) {
            (IEnumerable<string> files, bool hasParent) = Cache.List(requestRoute.Url.AbsolutePath);
            string html = @$"
                            <html>
                                <head><title>Index of {requestRoute.Url.AbsolutePath}</title></head>
                                <body>
                                    <h1>Index of {requestRoute.Url.AbsolutePath}</h1>
                                    <hr /><pre>"
                            + (hasParent ? @$"<a href=""../"">../</a>{Environment.NewLine}" : "")
                            + $"{string.Join(Environment.NewLine, files.Select(f => $"<a href=\"{f}\">{f}</a>"))}"
                        + @"</pre><hr />
                                </body>
                            </html>";
            SendResponseAsync(Response.MakeResponse(html, "text/html; charset=UTF-8", compress: Request.AcceptEncodings()));
        }

        /// <summary>
        /// Request in cache
        /// </summary>
        /// <param name="request"></param>
        /// <param name="content"></param>
        protected override void OnReceivedCachedRequest(HttpRequest request, byte[] content) {
            Activity? activity = CreateActivity();
            SetDefaultActivity(activity, $"CACHE {request.Url}", request, Id, Socket, server.TrustXHeaders);
            SendAsync(content);
            StopWithStatusCodeActivity(activity, 200, content.Length);
        }

        /// <summary>
        /// Request error
        /// </summary>
        /// <param name="request"></param>
        /// <param name="error"></param>
        protected override void OnReceivedRequestError(HttpRequest request, string error) {
            Activity? activity = CreateActivity();
            SetDefaultActivity(activity, $"ERROR {request.Url}", request, Id, Socket, server.TrustXHeaders);
            StopWithErrorActivity(activity);
        }

        #endregion http

        #region sse

        /// <summary>
        /// Is this session as SSE Session
        /// </summary>
        private bool IsSSESession = false;

        /// <summary>
        /// Flag the current Session as SSE Session
        /// and add it to the server SSESessions
        /// </summary>
        public void AddSSESession() {
            IsSSESession = true;
            server?.AddSSESession(this);
        }

        #endregion sse

        #region websocket

        /// <summary>
        /// Websocket connect
        /// </summary>
        /// <param name="request"></param>
        public override void OnWsConnected(HttpRequest request) {
            Activity? activity = CreateActivity();
            try {
                // parse : request.url to route
                Route requestRoute = new(request, Server.TrustXHeaders);
                SetDefaultActivity(activity, Request, requestRoute, Id, Socket, server.TrustXHeaders);
            }
            catch (Exception ex) {
                StopWithErrorActivity(activity, ex);
            }
        }

        /// <summary>
        /// Websocket received
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="size"></param>
        public override void OnWsReceived(byte[] buffer, long offset, long size) {
            Activity? activity = CreateActivity();
            try {
                SimpleWServer server = (SimpleWServer)Server;

                // parse buffer as WebSocketMessage
                string text = Encoding.UTF8.GetString(buffer, (int)offset, (int)size);
                WebSocketMessage message = JsonSerializer.Deserialize<WebSocketMessage>(text);
                this.jwt = message.jwt; // for later Controller.GetJwt()

                SetDefaultActivity(activity, message, Id, Socket);

                // register
                if (message.url == null) {
                    IWebUser wu = Controller.JwtToWebUser(this.jwt);
                    if (wu != null) {
                        server.RegisterWebSocketUser(Id, wu);
                    }
                    return;
                }

                // get first matching route
                Route routeMatch = server.Router.Match(message);
                if (routeMatch != null) {
                    if (routeMatch.Handler != null) {
                        routeMatch.Handler?.ExecuteMethod(this, Request, new object[] { message });
                        StopWithStatusCodeActivity(activity, 200, webuser: webuser);
                    }
                    return;
                }

                StopWithStatusCodeActivity(activity, 404);
            }
            catch (Exception ex) {
                StopWithStatusCodeActivity(activity, 500, exception: ex);
            }
        }

        /// <summary>
        /// Websocket disconnect
        /// </summary>
        public override void OnWsDisconnected() {
            Activity? activity = CreateActivity();
            ((SimpleWServer)Server).UnregisterWebSocketUser(Id);
            SetDefaultActivity(activity, $"DISCONNECT", Id);
            StopWithStatusCodeActivity(activity, 200);
        }

        #endregion websocket

        /// <summary>
        /// Server error
        /// </summary>
        /// <param name="error"></param>
        protected override void OnError(SocketError error) {
            if (server.EnableTelemetry) {
                Activity? activity = CreateActivity();
                SetDefaultActivity(activity, "Session OnError()", Id);
                activity.SetStatus(ActivityStatusCode.Error);
                ActivityTagsCollection tagsCollection = new() {
                    { "exception.type", nameof(SocketError) },
                };
                activity.AddEvent(new ActivityEvent("exception", default, tagsCollection));
                activity.Stop();
            }
        }

        /// <summary>
        /// OnDisconnected
        /// </summary>
        protected override void OnDisconnected() {
            // Remove the current Session from the server SSESessions
            if (this.IsSSESession) {
                server?.RemoveSSESession(this);
            }
            base.OnDisconnected();
        }

        #region OpenTelemetry

        /// <summary>
        /// ActivitySource
        /// </summary>
        protected readonly static ActivitySource ActivitySource = new("SimpleW", Assembly.GetExecutingAssembly().GetName().Version.ToString());

        /// <summary>
        /// Create an Activity for Telemetry
        /// </summary>
        /// <returns></returns>
        protected Activity? CreateActivity() {
            return server.EnableTelemetry ? ActivitySource.StartActivity() : null;
        }

        /// <summary>
        /// Set Default Properties of Activity for Trace
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="request"></param>
        /// <param name="requestRoute"></param>
        /// <param name="id"></param>
        /// <param name="socket"></param>
        /// <param name="trustXHeaders"></param>
        protected static void SetDefaultActivity(Activity activity, HttpRequest request, Route requestRoute, Guid id, Socket socket, bool trustXHeaders) {
            if (activity == null) {
                return;
            }

            activity.DisplayName = $"{requestRoute.Method} {requestRoute.Url.AbsolutePath}";

            activity.SetTag("network.transport", "TCP");
            activity.SetTag("network.type", "ipv4");

            activity.SetTag("server.address", (trustXHeaders ? request.Header("X-Forwarded-Host") ?? request.Header("Host") : null) ?? requestRoute.Url.Host); // frontend proxy
            activity.SetTag("server.port", requestRoute.Url.Port); // frontend proxy

            //activity.SetTag("server.socket.address", "192.168.1.22");   // derriere le proxy
            //activity.SetTag("server.socker.port", "2015");              // derriere le proxy

            activity.SetTag("url.path", requestRoute.Url.AbsolutePath);
            activity.SetTag("url.query", requestRoute.Url.Query);
            activity.SetTag("url.scheme", requestRoute.Url.Scheme);
            activity.SetTag("url.full", requestRoute.RawUrl);

            activity.SetTag("http.route", requestRoute.Url.AbsolutePath);
            activity.SetTag("http.request.method", requestRoute.Method);
            activity.SetTag("http.request.route", requestRoute.Url.AbsolutePath);
            activity.SetTag("http.request.body.size", request.BodyLength.ToString());

            activity.SetTag("session", id);

            activity.SetTag("client.address", (trustXHeaders ? request.Header("X-Real-IP") : null) ?? socket?.RemoteEndPoint.ToString());
            //activity.SetTag("client.port", address.Address);

            activity.SetTag("user_agent.original", request.Header("User-Agent"));

            //Activity.Current.Context.TraceId.Dump();
        }

        protected static void SetDefaultActivity(Activity activity, WebSocketMessage message, Guid id, Socket socket) {
            if (activity == null) {
                return;
            }

            activity.DisplayName = $"WEBSOCKET {message.url}";

            activity.SetTag("network.transport", "TCP");
            activity.SetTag("network.type", "ipv4");

            activity.SetTag("client.address", socket?.RemoteEndPoint.ToString());

            activity.SetTag("session", id);
        }

        protected static void SetDefaultActivity(Activity activity, string DisplayName, HttpRequest request, Guid id, Socket socket, bool trustXHeaders) {
            if (activity == null) {
                return;
            }

            activity.DisplayName = DisplayName;

            activity.SetTag("network.transport", "TCP");
            activity.SetTag("network.type", "ipv4");

            //activity.SetTag("server.address", request.Header("X-Forwarded-Host") ?? request.Header("Host") ?? requestRoute.Url.Host); // frontend proxy
            //activity.SetTag("server.port", requestRoute.Url.Port); // frontend proxy

            //activity.SetTag("server.socket.address", "192.168.1.22");   // derriere le proxy
            //activity.SetTag("server.socker.port", "2015");              // derriere le proxy
            string url = Route.FQURL(request, trustXHeaders);

            activity.SetTag("url.path", url);
            //activity.SetTag("url.query", requestRoute.Url.Query);
            //activity.SetTag("url.scheme", requestRoute.Url.Scheme);
            activity.SetTag("url.full", url);

            activity.SetTag("http.route", url);
            activity.SetTag("http.request.method", request.Method);
            activity.SetTag("http.request.route", url);
            activity.SetTag("http.request.body.size", request.BodyLength.ToString());

            activity.SetTag("session", id);

            activity.SetTag("client.address", (trustXHeaders ? request.Header("X-Real-IP") : null) ?? socket?.RemoteEndPoint.ToString());
            //activity.SetTag("client.port", address.Address);

            activity.SetTag("user_agent.original", request.Header("User-Agent"));
        }

        protected static void SetDefaultActivity(Activity activity, string DisplayName, Guid id) {
            if (activity == null) {
                return;
            }

            activity.DisplayName = DisplayName;
            activity.SetTag("session", id);
        }

        /// <summary>
        /// Stop Activity with Default Properties
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="response"></param>
        /// <param name="webuser"></param>
        /// <param name="exception"></param>
        protected static void StopWithStatusCodeActivity(Activity activity, HttpResponse response, IWebUser? webuser = null, Exception exception = null) {
            if (activity == null) {
                return;
            }

            activity.SetTag("webuser", webuser?.Login);
            activity.SetTag("http.response.status_code", response.Status.ToString());
            activity.SetTag("http.response.body.size", response.BodyLength.ToString());

            // opentelemetry convention indicate when status code not in (1xx, 2xx, 3xx)
            // the span status need to be set to "Error"
            // source : https://opentelemetry.io/docs/specs/otel/trace/semantic_conventions/http/
            if (response.Status >= 400) {
                activity.SetStatus(ActivityStatusCode.Error, (exception?.Message ?? $"http error {response.Status}"));
            }

            AddExceptionToActivity(activity, exception, default);

            // stop the trace
            activity.Stop();
        }

        protected static void StopWithStatusCodeActivity(Activity activity, int status_code, int? response_size = null, IWebUser? webuser = null, Exception? exception = null) {
            if (activity == null) {
                return;
            }

            activity.SetTag("http.response.status_code", status_code.ToString());

            if (response_size != null) {
                activity.SetTag("http.response.body.size", response_size.ToString());
            }

            // opentelemetry convention indicate when status code not in (1xx, 2xx, 3xx)
            // the span status need to be set to "Error"
            // source : https://opentelemetry.io/docs/specs/otel/trace/semantic_conventions/http/
            if (status_code >= 400) {
                activity.SetStatus(ActivityStatusCode.Error, (exception?.Message ?? $"http error {status_code}"));
            }

            activity.SetTag("webuser", webuser?.Login);

            AddExceptionToActivity(activity, exception, default);

            // stop the trace
            activity.Stop();
        }

        /// <summary>
        /// Stop Activity with Exception
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="exception"></param>
        protected static void StopWithErrorActivity(Activity activity, Exception? exception = null) {
            if (activity == null) {
                return;
            }

            activity.SetStatus(ActivityStatusCode.Error, exception?.Message);
            AddExceptionToActivity(activity, exception, default);

            // stop the trace
            activity.Stop();
        }

        /// <summary>
        /// source : https://opentelemetry.io/docs/specs/semconv/exceptions/exceptions-spans/
        /// source: https://github.com/open-telemetry/opentelemetry-dotnet/blob/86a6ba0b7f7ed1f5e84e5a6610e640989cd3ae9f/src/OpenTelemetry.Api/Trace/ActivityExtensions.cs#L88
        /// Add an exception to an activity
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="ex"></param>
        /// <param name="tags"></param>
        public static void AddExceptionToActivity(Activity activity, Exception? ex, ActivityTagsCollection? tags = null) {
            if (ex == null || activity == null) {
                return;
            }

            tags ??= new ActivityTagsCollection();
            tags.Add("exception.type", ex.GetType().FullName);
            tags.Add("exception.stacktrace", ex.ToString());

            if (!string.IsNullOrWhiteSpace(ex.Message)) {
                tags.Add("exception.message", ex.Message);
            }

            activity.AddEvent(new ActivityEvent(ex.Message, default, tags));
        }

        #endregion OpenTelemetry

    }

}
