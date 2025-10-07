using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// Delegate to redress WebUser
    /// </summary>
    /// <param name="webuser"></param>
    /// <returns>IWebUser (NOT NULL)</returns>
    public delegate IWebUser DelegateSetTokenWebUser(TokenWebUser webuser = null);

    /// <summary>
    /// <para>Controller is mandatory base class of all Controllers.</para>
    /// <para>Inherit from this class or subclass</para>
    /// </summary>
    public abstract class Controller {

        /// <summary>
        /// Gets the current HTTP Session
        /// </summary>
        protected ISimpleWSession Session;

        /// <summary>
        /// Gets the current HTTP Request
        /// </summary>
        protected HttpRequest Request;

        /// <summary>
        /// Gets the current prepared HTTP Response
        /// </summary>
        protected HttpResponse Response => Session.Response;

        /// <summary>
        /// Inject Session and Request after instanciation by Router and ControllerMethodExecutor.
        /// This way to avoid define a constructor in all inherited controllers
        /// </summary>
        /// <param name="session"></param>
        /// <param name="request"></param>
        public void Initialize(ISimpleWSession session, HttpRequest request) {
            Session = session;
            Request = request;

            // we need to set Session.webuser (mostly for logging purpose)
            Session.webuser = webuser;
        }

        #region webuser

        /// <summary>
        /// Token Secret Passphrase
        /// </summary>
        public static string TokenKey;
        /// <summary>
        /// Token Issuer url
        /// </summary>
        public static string TokenIssuer;
        /// <summary>
        /// Token Expiration in second
        /// </summary>
        public static double TokenExpiration;
        /// <summary>
        /// WebUserCallback
        /// </summary>
        public static DelegateSetTokenWebUser GetWebUserCallback;

        /// <summary>
        /// Get Current IWebUser
        /// </summary>
        protected IWebUser webuser {
            get {
                if (_webuser == null) {
                    _webuser = JwtToWebUser(GetJwt());
                }
                return _webuser;
            }
        }

        /// <summary>
        /// cache for webuser property
        /// </summary>
        private IWebUser _webuser;

        /// <summary>
        /// Return IWebUser from a jwt token
        /// The token is checked
        /// The webuser from the jwt is refresh with data from database
        /// If token or user is invalid, the fallback mode is used
        /// </summary>
        /// <param name="jwt">the string jwt</param>
        /// <returns></returns>
        public static IWebUser JwtToWebUser(string jwt) {
            try {
                TokenWebUser wu = jwt?.ValidateJwt<TokenWebUser>(TokenKey, TokenIssuer);
                if (GetWebUserCallback != null) {
                    return GetWebUserCallback(wu);
                }
                return wu ?? new WebUser();
            }
            catch {
                if (GetWebUserCallback != null) {
                    return GetWebUserCallback();
                }
                return new WebUser();
            }
        }

        #endregion webuser

        /// <summary>
        /// Get the JWT by order :
        ///     1. Session.jwt (websocket only)
        ///     2. Request url querystring "jwt" (api only)
        ///     3. Request http header "Authorization: bearer " (api only)
        /// </summary>
        protected virtual string GetJwt() {
            // 1. Session.jwt (websocket only)
            if (Session.jwt != null) {
                return Session.jwt;
            }

            // 2. Request url querystring "jwt" (api only)
            NameValueCollection qs = NetCoreServerExtension.ParseQueryString(Request.Url);
            string qs_jwt = qs["jwt"]?.ToString();
            if (!string.IsNullOrWhiteSpace(qs_jwt)) {
                return qs_jwt;
            }

            // 3. Request http header "Authorization: bearer " (api only)
            string header_jwt = Request.Header("Authorization");
            if (string.IsNullOrWhiteSpace(header_jwt) || !header_jwt.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            return header_jwt["Bearer ".Length..];
        }

        /// <summary>
        /// Override this Handler to call code before any Controller.Method()
        /// </summary>
        public virtual void OnBeforeMethod() { }

        #region sendResponse

        /// <summary>
        /// Send Response Async
        /// </summary>
        /// <param name="o">The Object o</param>
        public virtual void SendResponseAsync(object o) {
            if (o is HttpResponse response) {
                Session.SendResponseAsync(response);
                return;
            }
            SendResponseAsync(Encoding.UTF8.GetBytes(Session.Server.JsonEngine.Serialize(o)));
        }

        /// <summary>
        /// Send Response Async
        /// </summary>
        /// <param name="content">The String content</param>
        /// <param name="contentType">The String Content type (default is "application/json; charset=UTF-8")</param>
        protected virtual void SendResponseAsync(string content, string contentType = "application/json; charset=UTF-8") {
            SendResponseAsync(Encoding.UTF8.GetBytes(content), contentType);
        }

        /// <summary>
        /// Send Response Async
        /// </summary>
        /// <param name="content">byte[] content</param>
        /// <param name="contentType">The String Content type (default is "application/json; charset=UTF-8")</param>
        protected virtual void SendResponseAsync(byte[] content, string contentType = "application/json; charset=UTF-8") {
            Response.MakeResponse(content, contentType, Request.AcceptEncodings());
            Session.SendResponseAsync(Response);
        }

        #endregion sendResponse

        #region makeResponse

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The MemoryStream Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeDownloadResponse(MemoryStream content, string output_filename = null, string contentType = "text/plain; charset=UTF-8") {
            return MakeDownloadResponse(content.ToArray(), output_filename, contentType);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The string Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeDownloadResponse(string content, string output_filename = null, string contentType = "text/plain; charset=UTF-8") {
            return MakeDownloadResponse(Encoding.UTF8.GetBytes(content), output_filename, contentType);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The byte[] Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeDownloadResponse(byte[] content, string output_filename = null, string contentType = "text/plain; charset=UTF-8") {
            string[] compress = Request.Header("Accept-Encoding")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Response.MakeDownloadResponse(content, output_filename, contentType, compress);
        }

        /// <summary>
        /// Make Error Access response
        /// </summary>
        public HttpResponse MakeAccessResponse() {
            if (!webuser.Identity) {
                return Response.MakeErrorResponse(401);
            }
            return Response.MakeErrorResponse(403);
        }

        #endregion makeResponse

        #region sse

        /// <summary>
        /// Flag the current Session as SSE Session
        /// and add it to the server SSESessions
        /// Alias for Session.AddSSESession();
        /// </summary>
        public void AddSSESession() {
            Session.AddSSESession();
        }

        #endregion sse

    }

}