using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using SimpleW;


namespace NetCoreServer {

    /// <summary>
    /// HTTP response is used to create or process parameters of HTTP protocol response(status, headers, etc).
    /// </summary>
    /// <remarks>Not thread-safe.</remarks>
    public partial class HttpResponse {

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
        /// <param name="age"></param>
        public void SetCORSHeaders(int age = 86400) {
            SetCORSHeaders(this, age);
        }

        /// <summary>
        /// Set Header to response parameter when CORS is enabled
        /// </summary>
        /// <param name="response"></param>
        /// <param name="age"></param>
        public static void SetCORSHeaders(HttpResponse response, int age = 86400) {
            if (!string.IsNullOrWhiteSpace(cors_allow_origin)) {
                response.SetHeader("Access-Control-Allow-Origin", cors_allow_origin);
                response.SetHeader("Access-Control-Allow-Headers", cors_allow_headers);
                response.SetHeader("Access-Control-Allow-Methods", cors_allow_methods);
                response.SetHeader("Access-Control-Allow-Credentials", cors_allow_credentials);
                response.SetHeader("Access-Control-Max-Age", age.ToString());
            }
        }

        #endregion cors

        /// <summary>
        /// Cache for Session inject by ControllerMethodExecutor.Create()
        /// </summary>
        public ISimpleWSession Session { get; set; }

        #region makeResponse

        /// <summary>
        /// Make Response from string
        /// </summary>
        /// <param name="content">The string Content.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The string array of supported compress types (default null)</param>
        public HttpResponse MakeResponse(string content, string contentType = "application/json; charset=UTF-8", string[] compress = null) {
            Clear();
            SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                SetHeader("Content-Type", contentType);
            }

            if (compress != null) {
                foreach (string c in compress) {
                    try {
                        byte[] compressData = HttpResponse.Compress(Encoding.UTF8.GetBytes(content), c);
                        SetHeader("Content-Encoding", c);
                        SetBody(compressData);
                        return this;
                    }
                    catch { }
                }
            }

            SetBody(content);
            return this;
        }

        /// <summary>
        /// Make Response from byte[]
        /// </summary>
        /// <param name="content">byte[] Content.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The string array of supported compress types (default null)</param>
        public HttpResponse MakeResponse(byte[] content, string contentType = "application/json; charset=UTF-8", string[] compress = null) {
            Clear();
            SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                SetHeader("Content-Type", contentType);
            }

            if (compress != null) {
                foreach (string c in compress) {
                    try {
                        byte[] compressData = HttpResponse.Compress(content, c);
                        SetHeader("Content-Encoding", c);
                        SetBody(compressData);
                        return this;
                    }
                    catch { }
                }
            }

            SetBody(content);
            return this;
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The MemoryStream Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The string array of supported compress types (default null)</param>
        public HttpResponse MakeDownloadResponse(MemoryStream content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", string[] compress = null) {
            return MakeDownloadResponse(content.ToArray(), output_filename, contentType, compress);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The string Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The string array of supported compress types (default null)</param>
        public HttpResponse MakeDownloadResponse(string content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", string[] compress = null) {
            return MakeDownloadResponse(Encoding.UTF8.GetBytes(content), output_filename, contentType, compress);
        }

        /// <summary>
        /// Make Download response
        /// </summary>
        /// <param name="content">The byte[] Content.</param>
        /// <param name="output_filename">name of the download file.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The string array of supported compress types (default null)</param>
        public HttpResponse MakeDownloadResponse(byte[] content, string output_filename = null, string contentType = "text/plain; charset=UTF-8", string[] compress = null) {
            Clear();
            SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(output_filename)) {
                SetHeader("Content-Disposition", "attachment;filename=" + output_filename);
            }

            if (!string.IsNullOrWhiteSpace(contentType)) {
                SetHeader("Content-Type", contentType);
            }

            if (compress != null) {
                foreach (string c in compress) {
                    try {
                        byte[] compressData = HttpResponse.Compress(content, c);
                        SetHeader("Content-Encoding", c);
                        SetBody(compressData);
                        return this;
                    }
                    catch { }
                }
            }

            SetBody(content);
            return this;
        }

        /// <summary>
        /// Make UnAuthorized response
        /// </summary>
        /// <param name="content">Error content (default is "Server UnAuthorized Access")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeUnAuthorizedResponse(string content = "Server UnAuthorized Access", string contentType = "text/plain; charset=UTF-8") {
            return MakeErrorResponse(401, content, contentType);
        }

        /// <summary>
        /// Make Forbidden response
        /// </summary>
        /// <param name="content">Error content (default is "Server Forbidden Access")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeForbiddenResponse(string content = "Server Forbidden Access", string contentType = "text/plain; charset=UTF-8") {
            return MakeErrorResponse(403, content, contentType);
        }

        /// <summary>
        /// Make ServerInternalError response
        /// </summary>
        /// <param name="content">Error content (default is "Server Internal Error")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeInternalServerErrorResponse(string content = "Server Internal Error", string contentType = "text/plain; charset=UTF-8") {
            return MakeErrorResponse(500, content, contentType);
        }

        /// <summary>
        /// Make NotFound response
        /// </summary>
        /// <param name="content">Error content (default is "Not Found")</param>
        /// <param name="contentType">Error content type (default is "text/plain; charset=UTF-8")</param>
        public HttpResponse MakeNotFoundResponse(string content = "Not Found", string contentType = "text/plain; charset=UTF-8") {
            return MakeErrorResponse(404, content, contentType);
        }

        /// <summary>
        /// Make Redirect Tempory Response (status code 302)
        /// </summary>
        /// <param name="location">The string location.</param>
        public HttpResponse MakeRedirectResponse(string location) {
            Clear();
            SetBegin(302);
            SetCORSHeaders();

            SetHeader("Location", location);
            SetBody();

            return this;
        }

        /// <summary>
        /// Response for initializing Server Sent Events
        /// </summary>
        /// <returns></returns>
        public HttpResponse MakeServerSentEventsResponse() {
            Clear();
            SetBegin(200);
            SetCORSHeaders();

            SetHeader("Content-Type", "text/event-stream");
            SetHeader("Cache-Control", "no-cache");
            SetHeader("Connection", "keep-alive");

            return this;
        }

        #endregion makeResponse

        #region makeResponseDependingOnRequest

        /// <summary>
        /// Make Response from object
        /// </summary>
        /// <param name="content">object Content.</param>
        /// <param name="contentType">The contentType. (default is "text/plain; charset=UTF-8")</param>
        /// <param name="compress">The string array of supported compress types (default null)</param>
        public HttpResponse MakeResponse(object content, string contentType = "application/json; charset=UTF-8", string[] compress = null) {
            if (Session == null) {
                throw new InvalidOperationException("Response.MakeResponse(object content) must be called inside a Controller or Func");
            }
            Clear();
            SetBegin(200);
            SetCORSHeaders();

            if (!string.IsNullOrWhiteSpace(contentType)) {
                SetHeader("Content-Type", contentType);
            }

            if (compress != null) {
                foreach (string c in compress) {
                    try {
                        byte[] compressData = HttpResponse.Compress(Encoding.UTF8.GetBytes(Session.Server.JsonEngine.Serialize(content ?? string.Empty)), c);
                        SetHeader("Content-Encoding", c);
                        SetBody(compressData);
                        return this;
                    }
                    catch { }
                }
            }

            SetBody(Session.Server.JsonEngine.Serialize(content ?? string.Empty));
            return this;
        }

        /// <summary>
        /// Make Error Access response
        /// </summary>
        public HttpResponse MakeAccessResponse() {
            if (Session == null) {
                throw new InvalidOperationException("Response.MakeAccessResponse() must be called inside a Controller or Func");
            }
            if (!Session.webuser?.Identity ?? true) {
                return MakeUnAuthorizedResponse();
            }
            return MakeForbiddenResponse();
        }

        #endregion makeResponseDependingOnRequest

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

        #endregion helper

    }
}
