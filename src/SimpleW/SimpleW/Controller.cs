using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization;
using System.Text;
using NetCoreServer;
using Newtonsoft.Json;


namespace SimpleW {

    public delegate IWebUser DelegateSetTokenWebUser(Guid id = new Guid());

    /// <summary>
    /// <para>Controller is mandatory base class of all Controllers.</para>
    /// <para>Inherit from this class or subclass</para>
    /// </summary>
    public abstract class Controller {

        /// <summary>
        /// Gets the current HTTP Session
        /// </summary>
        protected SimpleWSession Session;

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
        public void Initialize(SimpleWSession session, HttpRequest request) {
            Session = session;
            Request = request;
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
                if (!_webuser_init) {
                    _webuser_init = true;
                    SetWebUser();
                }
                return _webuser;
            }
        }
        
        /// <summary>
        /// Current webuser instance
        /// </summary>
        private IWebUser _webuser;
        
        /// <summary>
        /// Flag : cache _webuser to avoid multi request
        /// </summary>
        private bool _webuser_init;

        /// <summary>
        /// Set the _webuser
        /// </summary>
        protected void SetWebUser() {
            _webuser = JwtToWebUser(GetJwt());
        }

        /// <summary>
        /// Return IWebUser from a jwt token
        /// The token is checked
        /// The webuser from the jwt is refresh with data from database
        /// If token or user is invalid, the fallback mode is used
        /// </summary>
        /// <param name="jwt">the string jwt</param>
        /// <returns></returns>
        public static IWebUser JwtToWebUser(string jwt) {
            // get webuser and update property (to avoid to do it in each controller)
            try {
                var wu = jwt?.ValidateJwt<TokenWebUser>(TokenKey, TokenIssuer);

                // jwt token is valid and webuser must be refresh
                if (wu != null) {
                    return wu.Refresh
                                ? GetWebUserCallback(wu.Id)
                                : wu;
                }
                // try to get default TokenWebuser if defined in GetWebUserCallback
                else if (GetWebUserCallback != null) {
                    return GetWebUserCallback();
                }
                else {
                    return new WebUser();
                }
            }
            catch {
                return new WebUser();
            }
        } 

        /// <summary>
        /// Get the JWT by order :
        ///     1. Session.jwt (websocket only)
        ///     2. Request url querystring "jwt" (api only)
        ///     3. Request http header "Authorization: bearer " (api only)
        /// </summary>
        protected string GetJwt() {
            // websocket
            if (Session.jwt != null) {
                return Session.jwt;
            }
            // api
            var route = new Route(Request);
            var qs = Route.ParseQueryString(route?.Url?.Query);
            var jwt = qs["jwt"]?.ToString();
            return jwt ?? Request.GetBearer();
        }

        #endregion webuser

        /// <summary>
        /// Override this Handler to call code before any Controller.Method()
        /// </summary>
        public virtual void OnBeforeMethod() { }

        #region response

        /// <summary>
        /// Set Header when CORS is enabled
        /// </summary>
        protected void SetCORSHeaders() {
            if (!string.IsNullOrWhiteSpace(((SimpleWServer)Session.Server).cors_allow_origin)) {
                Response.SetHeader("Access-Control-Allow-Origin", ((SimpleWServer)Session.Server).cors_allow_origin);
                Response.SetHeader("Access-Control-Allow-Headers", ((SimpleWServer)Session.Server).cors_allow_headers);
                Response.SetHeader("Access-Control-Allow-Methods", ((SimpleWServer)Session.Server).cors_allow_methods);
                Response.SetHeader("Access-Control-Allow-Credentials", ((SimpleWServer)Session.Server).cors_allow_credentials);
                Response.SetHeader("Access-Control-Max-Age", "86400");
            }
        }

        /// <summary>
        /// Override this Handler to change how RouteMethod which return object should to.
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
            SendResponseAsync(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(o)));
        }

        /// <summary>
        /// Send Response Async
        /// </summary>
        /// <param name="content">The String content</param>
        /// <param name="contentType">The String Content type (default is "application/json; charset=UTF-8")</param>
        /// <param name="compress">The String Compression type (default true, it uses gzip or deflate depending request support content-encoding)</param>
        protected virtual void SendResponseAsync(string content, string contentType = "application/json; charset=UTF-8", bool compress = true) {
            SendResponseAsync(Encoding.UTF8.GetBytes(content), contentType, compress);
        }

        /// <summary>
        /// Send Response Async
        /// </summary>
        /// <param name="content">byte[] content</param>
        /// <param name="contentType">The String Content type (default is "application/json; charset=UTF-8")</param>
        /// <param name="compress">The String Compression type (default true, it uses gzip or deflate depending request support content-encoding)</param>
        protected virtual void SendResponseAsync(byte[] content, string contentType = "application/json; charset=UTF-8", bool compress = true) {
            Response.Clear();
            Response.SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                Response.SetHeader("Content-Type", contentType);
            }

            if (compress) {
                var compressTypes = Request.Header("Accept-Encoding")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (compressTypes != null) {
                    foreach (var compressType in compressTypes) {
                        try {
                            var compressData = Compress(content, compressType);
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
        /// Make Response from object with JsonSerializerSettings.Context.streamingContextObject StreamingContextStates.Other
        /// </summary>
        /// <param name="content">The object Content.</param>
        /// <param name="streamingContextObject">The JsonSerializerSettings.StreamingContext.AdditionnalObject (default is null)</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeResponse(object content, object? streamingContextObject = null, string contentType = "application/json; charset=UTF-8") {
            return MakeResponse(content, 
                                new JsonSerializerSettings {
                                    Context = new StreamingContext(StreamingContextStates.Other, streamingContextObject)
                                },
                                contentType);
        }

        /// <summary>
        /// Make Response from object with JsonSerializerSettings.Context.streamingContextObject StreamingContextStates.Other
        /// </summary>
        /// <param name="content">The object Content.</param>
        /// <param name="settings">The JsonSerializerSettings settings (default is null)</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeResponse(object content, JsonSerializerSettings settings = null, string contentType = "application/json; charset=UTF-8") {
            Response.Clear();
            Response.SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                Response.SetHeader("Content-Type", contentType);
            }

            Response.SetBody(JsonConvert.SerializeObject(content, settings));
            return Response;
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The MemoryStream Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The String Compression type (default true, it uses gzip or deflate depending request support content-encoding)</param>
        public HttpResponse MakeDownloadResponse(MemoryStream content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true) {
            return MakeDownloadResponse(content.ToArray() , output_filename, contentType, compress);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The string Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The String Compression type (default true, it uses gzip or deflate depending request support content-encoding)</param>
        public HttpResponse MakeDownloadResponse(string content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", bool compress = true) {
            return MakeDownloadResponse(Encoding.UTF8.GetBytes(content), output_filename, contentType, compress);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The byte[] Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The String Compression type (default true, it uses gzip or deflate depending request support content-encoding)</param>
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
                var compressTypes = Request.Header("Accept-Encoding")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var compressType in compressTypes) {
                    try {
                        var compressData = Compress(content, compressType);
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

        #endregion special

        #region helper

        /// <summary>
        /// Compress byte array using gzip or deflate algorithm.
        /// </summary>
        /// <param name="data">The Byte Array data.</param>
        /// <param name="algorithm">The String algorithm ("gzip" as default or "deflate").</param>
        public static byte[] Compress(byte[] data, string algorithm = "gzip") {
            using (var compressedStream = new MemoryStream()) {
                if (algorithm == "gzip") {
                    using (var zipStream = new GZipStream(compressedStream, CompressionMode.Compress)) {
                        zipStream.Write(data, 0, data.Length);
                        zipStream.Close();
                        return compressedStream.ToArray();
                    }
                }

                else if (algorithm == "deflate") {
                    using (var deflateStream = new DeflateStream(compressedStream, CompressionMode.Compress)) {
                        deflateStream.Write(data, 0, data.Length);
                        deflateStream.Close();
                        return compressedStream.ToArray();
                    }
                }

                throw new ArgumentException("Invalid compression type support !", nameof(algorithm));
            }
        }

        #endregion helper

    }

}