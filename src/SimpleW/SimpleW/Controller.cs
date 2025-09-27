using System;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
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
            Route route = new(Request);
            NameValueCollection qs = Route.ParseQueryString(route?.Url?.Query);
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

        #region response

        #region cors

        /// <summary>
        /// CORS Header Origin
        /// </summary>
        public static string cors_allow_origin { get; set; }
        /// <summary>
        /// CORS Header headers
        /// </summary>
        public static string cors_allow_headers { get; set; }
        /// <summary>
        /// CORS Header methods
        /// </summary>
        public static string cors_allow_methods { get; set; }
        /// <summary>
        /// CORS Header credentials
        /// </summary>
        public static string cors_allow_credentials { get; set; }

        /// <summary>
        /// Set Header when CORS is enabled
        /// </summary>
        protected void SetCORSHeaders() {
            if (!string.IsNullOrWhiteSpace(cors_allow_origin)) {
                Response.SetHeader("Access-Control-Allow-Origin", cors_allow_origin);
                Response.SetHeader("Access-Control-Allow-Headers", cors_allow_headers);
                Response.SetHeader("Access-Control-Allow-Methods", cors_allow_methods);
                Response.SetHeader("Access-Control-Allow-Credentials", cors_allow_credentials);
                Response.SetHeader("Access-Control-Max-Age", "86400");
            }
        }

        #endregion cors

        /// <summary>
        /// Override this Handler to change how RouteMethod which return object should do
        /// </summary>
        protected virtual void SendResponseAsync() {
            Session.SendResponseAsync();
        }

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
        /// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
        protected virtual void SendResponseAsync(string content, string contentType = "application/json; charset=UTF-8", bool compress = true) {
            SendResponseAsync(Encoding.UTF8.GetBytes(content), contentType, compress);
        }

        /// <summary>
        /// Send Response Async
        /// </summary>
        /// <param name="content">byte[] content</param>
        /// <param name="contentType">The String Content type (default is "application/json; charset=UTF-8")</param>
        /// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
        protected virtual void SendResponseAsync(byte[] content, string contentType = "application/json; charset=UTF-8", bool compress = true) {
            Response.Clear();
            Response.SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                Response.SetHeader("Content-Type", contentType);
            }

            if (compress) {
                string[] compressTypes = Request.Header("Accept-Encoding")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (compressTypes != null) {
                    foreach (string compressType in compressTypes) {
                        try {
                            byte[] compressData = Compress(content, compressType);
                            Response.SetHeader("Content-Encoding", compressType);
                            Response.SetBody(compressData);
                            Session.SendResponseAsync(Response);
                            return;
                        }
                        catch { }
                    }
                }
            }

            Response.SetBody(content);
            Session.SendResponseAsync(Response);
        }

        #endregion response

        #region special

        /// <summary>
        /// Make Response from string
        /// </summary>
        /// <param name="content">The string Content.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeResponse(string content, string contentType = "application/json; charset=UTF-8") {
            Response.Clear();
            Response.SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                Response.SetHeader("Content-Type", contentType);
            }

            Response.SetBody(content);
            return Response;
        }

        /// <summary>
        /// Make Response from byte[]
        /// </summary>
        /// <param name="content">byte[] Content.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeResponse(byte[] content, string contentType = "application/json; charset=UTF-8") {
            Response.Clear();
            Response.SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                Response.SetHeader("Content-Type", contentType);
            }

            Response.SetBody(content);
            return Response;
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The MemoryStream Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
        public HttpResponse MakeDownloadResponse(MemoryStream content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true) {
            return MakeDownloadResponse(content.ToArray() , output_filename, contentType, compress);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The string Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
        public HttpResponse MakeDownloadResponse(string content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true) {
            return MakeDownloadResponse(Encoding.UTF8.GetBytes(content), output_filename, contentType, compress);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The byte[] Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">To enable compression (default true, it uses gzip or deflate depending request support content-encoding)</param>
        public HttpResponse MakeDownloadResponse(byte[] content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true) {
            Response.Clear();
            Response.SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(output_filename)) {
                Response.SetHeader("Content-Disposition", "attachment;filename=" + output_filename);
            }

            if (!string.IsNullOrWhiteSpace(contentType)) {
                Response.SetHeader("Content-Type", contentType);
            }

            if (compress) {
                string[] compressTypes = Request.Header("Accept-Encoding")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (string compressType in compressTypes) {
                    try {
                        byte[] compressData = Compress(content, compressType);
                        Response.SetHeader("Content-Encoding", compressType);
                        Response.SetBody(compressData);
                        return Response;
                    }
                    catch { }
                }
            }

            Response.SetBody(content);
            return Response;
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

        /// <summary>
        /// Make UnAuthorized response
        /// </summary>
        /// <param name="content">Error content (default is "Server UnAuthorized Access")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeUnAuthorizedResponse(string content = "Server UnAuthorized Access", string contentType = "text/plain; charset=UTF-8") {
            return Response.MakeErrorResponse(401, content, contentType);
        }

        /// <summary>
        /// Make Forbidden response
        /// </summary>
        /// <param name="content">Error content (default is "Server Forbidden Access")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeForbiddenResponse(string content = "Server Forbidden Access", string contentType = "text/plain; charset=UTF-8") {
            return Response.MakeErrorResponse(403, content, contentType);
        }

        /// <summary>
        /// Make ServerInternalError response
        /// </summary>
        /// <param name="content">Error content (default is "Server Internal Error")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeInternalServerErrorResponse(string content = "Server Internal Error", string contentType = "text/plain; charset=UTF-8") {
            return Response.MakeErrorResponse(500, content, contentType);
        }

        /// <summary>
        /// Make NotFound response
        /// </summary>
        /// <param name="content">Error content (default is "Not Found")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeNotFoundResponse(string content = "Not Found", string contentType = "text/plain; charset=UTF-8") {
            return Response.MakeErrorResponse(404, content, contentType);
        }

        /// <summary>
        /// Make Redirect Tempory Response (status code 302)
        /// </summary>
        /// <param name="location">The string location.</param>
        public HttpResponse MakeRedirectResponse(string location) {
            Response.Clear();
            Response.SetBegin(302);
            SetCORSHeaders();

            Response.SetHeader("Location", location);
            Response.SetBody();

            return Response;
        }

        /// <summary>
        /// Response for initializing Server Sent Events
        /// </summary>
        /// <returns></returns>
        public HttpResponse MakeServerSentEventsResponse() {
            Response.Clear();
            Response.SetBegin(200);
            SetCORSHeaders();

            Response.SetHeader("Content-Type", "text/event-stream");
            Response.SetHeader("Cache-Control", "no-cache");
            Response.SetHeader("Connection", "keep-alive");

            return Response;
        }

        #endregion special

        #region helper

        /// <summary>
        /// Compress byte array using gzip or deflate algorithm.
        /// </summary>
        /// <param name="data">The Byte Array data.</param>
        /// <param name="algorithm">The String algorithm (supported by priority : br, gzip, deflate).</param>
        public static byte[] Compress(byte[] data, string algorithm) {
            using (MemoryStream compressedStream = new()) {

                if (algorithm == "br") {
                    using (BrotliStream brotliStream = new(compressedStream, CompressionMode.Compress, leaveOpen: true)) {
                        brotliStream.Write(data, 0, data.Length);
                    }
                    return compressedStream.ToArray();
                }

                else if (algorithm == "gzip") {
                    using (GZipStream zipStream = new(compressedStream, CompressionMode.Compress)) {
                        zipStream.Write(data, 0, data.Length);
                        zipStream.Close();
                        return compressedStream.ToArray();
                    }
                }

                else if (algorithm == "deflate") {
                    using (DeflateStream deflateStream = new(compressedStream, CompressionMode.Compress)) {
                        deflateStream.Write(data, 0, data.Length);
                        deflateStream.Close();
                        return compressedStream.ToArray();
                    }
                }

                throw new ArgumentException("Invalid compression type support !", nameof(algorithm));
            }
        }

        /// <summary>
        /// Flag the current Session as SSE Session
        /// and add it to the server SSESessions
        /// Alias for Session.AddSSESession();
        /// </summary>
        public void AddSSESession() {
            Session.AddSSESession();
        }

        #endregion helper

    }

}