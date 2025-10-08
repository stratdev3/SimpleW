using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HttpMultipartParser;
using LitJWT;
using LitJWT.Algorithms;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// NetCoreServer Extension helpers methods
    /// </summary>
    public static partial class NetCoreServerExtension {

        #region properties

        /// <summary>
        /// Return Header Value for a specified header name for a Request
        /// </summary>
        /// <param name="request">The HttpRequest request</param>
        /// <param name="name">The Header Name.</param>
        /// <returns><c>string value</c> of the header if exists in the Request; otherwise, <c>null</c>.</returns>
        public static string Header(this HttpRequest request, string name) {
            for (int i = 0; i < request.Headers; i++) {
                (string headerName, string headerValue) = request.Header(i);
                if (string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase)) {
                    return headerValue;
                }
            }
            return null;
        }

        /// <summary>
        /// Return the string[] of accept encodings for a Request
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public static string[] AcceptEncodings(this HttpRequest request) {
            return request.HeaderAcceptEncodings?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        #endregion properties

        /// <summary>
        /// Json Serializer/Deserializer
        /// </summary>
        public static IJsonEngine JsonEngine { get; set; } = new SystemTextJsonEngine(SystemTextJsonEngine.OptionsSimpleWBuilder());

        #region parse

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <param name="jsonEngine">the json library to handle serialization/deserialization</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMap<TModel>(this HttpRequest request, TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, IJsonEngine jsonEngine = null) {
            string contentType = request.HeaderContentType;
            string body = request.Body;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }

            // use default if null
            jsonEngine ??= JsonEngine;

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new Exception($"multipart/form-data contentType must be parsed with {nameof(BodyFile)}() method");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, object> kv = BodyForm(body);
                body = jsonEngine.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {
                return JsonMap(body, model, includeProperties, excludeProperties, jsonEngine);
            }

            return true;
        }

        /// <summary>
        /// Update the anonymous model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Anonymous Model instance to populate.</param>
        /// <param name="jsonEngine">the json library to handle serialization/deserialization</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMapAnonymous<TModel>(this HttpRequest request, ref TModel model, IJsonEngine jsonEngine = null) {
            string contentType = request.HeaderContentType;
            string body = request.Body;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }


            // use default if null
            jsonEngine ??= JsonEngine;

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new Exception($"multipart/form-data contentType must be parsed with {nameof(BodyFile)}() method");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, object> kv = BodyForm(body);
                body = jsonEngine.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {
                // deserialize AnonymousType
                model = jsonEngine.DeserializeAnonymous(body, model);
            }

            return true;
        }

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="json">The json string.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <param name="jsonEngine">the json library to handle serialization/deserialization (default: JsonEngine)</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool JsonMap<TModel>(string json, TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, IJsonEngine jsonEngine = null) {

            if (string.IsNullOrWhiteSpace(json)) {
                return false;
            }

            // use default if null
            jsonEngine ??= JsonEngine;

            // deserialize and populate
            jsonEngine.Populate(json, model, includeProperties, excludeProperties);

            return true;
        }

        /// <summary>
        /// Parses multipart/form-data and contentType given the request body (Request.InputStream)
        /// Please note the underlying input stream is not rewindable.
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <returns>MultipartFormDataParser</returns>
        public static MultipartFormDataParser BodyFile(this HttpRequest request) {
            MemoryStream stream = new(request.BodyBytes);
            return MultipartFormDataParser.Parse(stream);
        }

        /// <summary>
        /// Parses application/x-www-form-urlencoded contentType given the request body string.
        /// </summary>
        /// <param name="requestBody">The string request body.</param>
        /// <returns>key/value data</returns>
        private static Dictionary<string, object> BodyForm(string requestBody) {

            // verify there is data to parse
            if (string.IsNullOrWhiteSpace(requestBody)) {
                return null;
            }

            // define a character for KV pairs
            char[] kvpSeparator = new[] { '=' };

            // Create the result object
            Dictionary<string, object> resultDictionary = new();

            // Split the request body into key-value pair strings
            string[] keyValuePairStrings = requestBody.Split('&');

            foreach (string kvps in keyValuePairStrings) {
                // Skip KVP strings if they are empty
                if (string.IsNullOrWhiteSpace(kvps)) {
                    continue;
                }

                // Split by the equals char into key values.
                // Some KVPS will have only their key, some will have both key and value
                // Some other might be repeated which really means an array
                string[] kvpsParts = kvps.Split(kvpSeparator, 2);

                // We don't want empty KVPs
                if (kvpsParts.Length == 0) {
                    continue;
                }

                // Decode the key and the value. Discard Special Characters
                string? key = WebUtility.UrlDecode(kvpsParts[0]);
                if (!string.IsNullOrWhiteSpace(key) && key.IndexOf("[]", StringComparison.OrdinalIgnoreCase) > 0) {
                    key = key[..key.IndexOf("[]", StringComparison.OrdinalIgnoreCase)];
                }

                string value = kvpsParts.Length >= 2 ? WebUtility.UrlDecode(kvpsParts[1]) : null;

                // If the result already contains the key, then turn the value of that key into a List of strings
                if (resultDictionary.TryGetValue(key, out object getValue)) {
                    // Check if this key has a List value already
                    if (getValue is not List<string> listValue) {
                        // if we don't have a list value for this key, then create one and add the existing item
                        listValue = new List<string>() { getValue as string };
                        resultDictionary[key] = listValue;
                    }

                    // By this time, we are sure listValue exists. Simply add the item
                    listValue.Add(value);
                }
                else {
                    // Simply set the key to the parsed value
                    resultDictionary[key] = value;
                }
            }

            return resultDictionary;
        }

        #endregion parse

        #region jwtsecuritytoken

        /// <summary>
        /// Validate a JWT Token (and expiration date) and return the underlying T type
        /// Success : return an instance of T class and map jwt payload to all public properties
        /// Invalid/Error : return null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="token"></param>
        /// <param name="key"></param>
        /// <param name="issuer"></param>
        /// <returns>T</returns>
        public static T ValidateJwt<T>(this string token, string key, string issuer = null) where T : class {
            if (string.IsNullOrWhiteSpace(token)) {
                return null;
            }

            JwtDecoder decoder = new(new HS256Algorithm(Encoding.UTF8.GetBytes(key)));
            try {
                // TODO : check issuer
                DecodeResult result = decoder.TryDecode(token, x => JsonEngine.Deserialize<T>(Encoding.UTF8.GetString(x)), out var t);
                if (result == DecodeResult.Success) {
                    return t;
                }
                return null;
            }
            catch {
                return null;
            }
        }

        /// <summary>
        /// Create a JWT token
        /// </summary>
        /// <param name="payload">The Dictionary payload</param>
        /// <param name="key">The string secret key from which the token is sign</param>
        /// <param name="issuer">The string issuer which is allowed</param>
        /// <param name="expiration">The int expiration time in second (default: 15 minutes)</param>
        /// <returns>The token string</returns>
        public static string CreateJwt(Dictionary<string, object> payload, string key, string issuer = null, double expiration = 15*60) {
            payload.Add("iss", issuer);

            JwtEncoder encoder = new(new HS256Algorithm(Encoding.UTF8.GetBytes(key)));

            string token = encoder.Encode(
                payload,
                DateTimeOffset.UtcNow.AddSeconds(expiration), // expiration "exp"
                (x, writer) => writer.Write(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(x))
            );
            return token;
        }

        /// <summary>
        /// Create a JWT token
        /// </summary>
        /// <param name="payload">The Dictionary payload</param>
        /// <param name="key">The string secret key from which the token is sign</param>
        /// <param name="expiration">The datetime expiration</param>
        /// <param name="issuer">The string issuer which is allowed</param>
        /// <returns>The token string</returns>
        public static string CreateJwt(Dictionary<string, object> payload, string key, DateTime expiration, string issuer = null) {
            payload.Add("iss", issuer);

            JwtEncoder encoder = new(new HS256Algorithm(Encoding.UTF8.GetBytes(key)));

            string token = encoder.Encode(
                payload,
                expiration, // expiration "exp"
                (x, writer) => writer.Write(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(x))
            );
            return token;
        }

        /// <summary>
        /// Create a JWT token
        /// </summary>
        /// <param name="webuser">The IWebUser</param>
        /// <param name="key">The string secret key from which the token is sign</param>
        /// <param name="issuer">The string issuer which is allowed</param>
        /// <param name="expiration">The int expiration time in second (default: 15 minutes)</param>
        /// <param name="refresh">The bool refresh</param>
        /// <returns>The token string</returns>
        public static string CreateJwt(IWebUser webuser, string key, string issuer = null, double expiration = 15 * 60, bool refresh = true) {
            Dictionary<string, object> payload = new() {
                { nameof(IWebUser.Identity), webuser.Identity },
                { nameof(IWebUser.Id), webuser.Id },
                { nameof(IWebUser.Login), webuser.Login },
                { nameof(IWebUser.Mail), webuser.Mail },
                { nameof(IWebUser.FullName), webuser.FullName },
                { nameof(IWebUser.Profile), webuser.Profile },
                { nameof(IWebUser.Roles), webuser.Roles },
                { nameof(IWebUser.Preferences), webuser.Preferences },
                { nameof(TokenWebUser.Refresh), refresh },
            };

            return CreateJwt(payload, key, issuer, expiration);
        }

        /// <summary>
        /// Create a JWT token
        /// </summary>
        /// <param name="webuser">The IWebUser</param>
        /// <param name="key">The string secret key from which the token is sign</param>
        /// <param name="issuer">The string issuer which is allowed</param>
        /// <param name="expiration">The datetime expiration</param>
        /// <param name="refresh">The bool refresh</param>
        /// <returns>The token string</returns>
        public static string CreateJwt(IWebUser webuser, string key, DateTime expiration, string issuer = null,bool refresh = true) {
            Dictionary<string, object> payload = new() {
                { nameof(IWebUser.Identity), webuser.Identity },
                { nameof(IWebUser.Id), webuser.Id },
                { nameof(IWebUser.Login), webuser.Login },
                { nameof(IWebUser.Mail), webuser.Mail },
                { nameof(IWebUser.FullName), webuser.FullName },
                { nameof(IWebUser.Profile), webuser.Profile },
                { nameof(IWebUser.Roles), webuser.Roles },
                { nameof(IWebUser.Preferences), webuser.Preferences },
                { nameof(TokenWebUser.Refresh), refresh },
            };

            return CreateJwt(payload, key, expiration, issuer);
        }

        #endregion jwtsecuritytoken

        #region InlineFunc

        /// <summary>
        /// Act like AddStaticContent but direct file access and without cache or filewatcher
        /// </summary>
        /// <param name="documentRoot"></param>
        /// <param name="prefix"></param>
        /// <param name="filter"></param>
        /// <param name="session"></param>
        /// <param name="request"></param>
        public static object AddStaticContentNoCache(string documentRoot, string prefix, string filter, ISimpleWSession session, HttpRequest request) {
            try {
                // define docRoot
                documentRoot = Path.GetFullPath(documentRoot);

                // sanitize url
                string rawUrl = request.Url ?? "/";
                string urlPath = rawUrl.Split('?', '#')[0].Replace('\\', '/');

                // prefix protection
                if (!urlPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                    return session.Response.MakeUnAuthorizedResponse("invalid prefix");
                }

                // path
                string relativePath = urlPath[prefix.Length..].Trim('/');
                string fullPath = Path.GetFullPath(Path.Join(documentRoot, relativePath));

                // directory traversal protection
                if (!fullPath.StartsWith(documentRoot, StringComparison.OrdinalIgnoreCase)) {
                    return session.Response.MakeUnAuthorizedResponse("access denied");
                }

                // compile le filtre en regex
                string regexPattern = "^" + Regex.Escape(filter).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                Regex filterRegex = new(regexPattern, RegexOptions.IgnoreCase);

                if (Directory.Exists(fullPath)) {
                    string defaultDocumentFullPath = Path.Join(fullPath, session.Server.DefaultDocument);
                    if (File.Exists(defaultDocumentFullPath) && filterRegex.IsMatch(Path.GetFileName(defaultDocumentFullPath))) {
                        return session.Response.MakeResponse(File.ReadAllBytes(defaultDocumentFullPath), Path.GetFileName(defaultDocumentFullPath));
                    }
                    if (!session.Server.AutoIndex) {
                        return session.Response.MakeNotFoundResponse();
                    }

                    if (urlPath[^1] != '/') {
                        urlPath += "/";
                    }

                    string html = AutoIndexPage(request.Url, urlPath, Directory.GetFileSystemEntries(fullPath, filter), !string.Equals(fullPath, documentRoot, StringComparison.OrdinalIgnoreCase), (entry) => Path.GetFileName(entry));
                    return session.Response.MakeGetResponse(html, "text/html; charset=UTF-8");
                }
                else if (File.Exists(fullPath) && filterRegex.IsMatch(Path.GetFileName(fullPath))) {
                    return session.Response.MakeResponse(File.ReadAllBytes(fullPath), Path.GetFileName(fullPath));
                }
                else {
                    return session.Response.MakeNotFoundResponse("directory or file not found");
                }
            }
            catch (Exception ex) {
                return session.Response.MakeInternalServerErrorResponse(ex.Message);
            }
        }

        /// <summary>
        /// Return a Index Page from entries
        /// </summary>
        /// <param name="absoluteUrl"></param>
        /// <param name="relativeUrl"></param>
        /// <param name="entries"></param>
        /// <param name="hasParent"></param>
        /// <param name="handler"></param>
        /// <returns></returns>
        public static string AutoIndexPage(string absoluteUrl, string relativeUrl, IEnumerable<string> entries, bool hasParent = false, Func<string, string> handler = null) {

            StringBuilder sb = new();
            sb.AppendLine($"""
                <!DOCTYPE html>
                <html>
                    <head><title>Index of {absoluteUrl}</title></head>
                    <body>
                        <h1>Index of {absoluteUrl}</h1>
                        <hr /><pre>
            """);
            if (hasParent) {
                sb.AppendLine($@"<a href=""{relativeUrl}../"">../</a>");
            }
            foreach (string entry in entries) {
                string e = handler != null ? handler(entry) : entry;
                sb.AppendLine($@"<a href=""{relativeUrl}{e}"">{e}</a>");
            }
            sb.AppendLine($"""
                        </pre><hr />
                    </body>
                </html>
            """);

            return sb.ToString();
        }

        #endregion InlineFunc

        #region helpers

        /// <summary>
        /// Parses the query string into dictionary of key/value
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static Dictionary<string, string> ParseQueryString(string url) {
            Dictionary<string, string> qs = new(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(url)) {
                return qs;
            }

            int qIndex = url.IndexOf('?');
            ReadOnlySpan<char> span = (qIndex >= 0 && qIndex < url.Length - 1)
                                            ? url.AsSpan(qIndex + 1)
                                            : url.AsSpan();

            int start = 0;
            while (start < span.Length) {
                int ampIndex = span.Slice(start).IndexOf('&');
                ReadOnlySpan<char> pair = ampIndex >= 0 ? span.Slice(start, ampIndex) : span.Slice(start);

                int eqIndex = pair.IndexOf('=');
                if (eqIndex > 0) {
                    string key = pair.Slice(0, eqIndex).ToString();
                    string value = pair.Slice(eqIndex + 1).ToString();
                    // the url must already be unescape
                    //key = Uri.UnescapeDataString(key.Replace("+", " "));
                    //value = Uri.UnescapeDataString(value.Replace("+", " "));
                    qs[key] = value;
                }
                else if (pair.Length > 0) {
                    // key without value
                    string key = pair.ToString();
                    qs[key] = string.Empty;
                }

                if (ampIndex < 0) {
                    break;
                }
                start += ampIndex + 1;
            }

            return qs;
        }

        /// <summary>
        /// Parses the query string to find the key and return value
        /// </summary>
        /// <param name="url"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool ParseQueryString(string url, string key, out string value) {
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key)) {
                return false;
            }

            int qIndex = url.IndexOf('?');
            ReadOnlySpan<char> span = (qIndex >= 0 && qIndex < url.Length - 1)
                                        ? url.AsSpan(qIndex + 1)
                                        : url.AsSpan();

            ReadOnlySpan<char> keySpan = key.AsSpan();

            int start = 0;
            while (start < span.Length) {
                int ampIndex = span.Slice(start).IndexOf('&');
                ReadOnlySpan<char> pair = ampIndex >= 0 ? span.Slice(start, ampIndex) : span.Slice(start);

                int eqIndex = pair.IndexOf('=');
                if (eqIndex > 0) {
                    ReadOnlySpan<char> kSpan = pair.Slice(0, eqIndex);
                    ReadOnlySpan<char> vSpan = pair.Slice(eqIndex + 1);

                    if (kSpan.Equals(keySpan, StringComparison.OrdinalIgnoreCase)) {
                        value = vSpan.ToString();
                        return true;
                    }
                }
                else if (pair.Length > 0) {
                    // key without value
                    if (pair.Equals(keySpan, StringComparison.OrdinalIgnoreCase)) {
                        value = string.Empty;
                        return true;
                    }
                }

                if (ampIndex < 0) {
                    break;
                }
                start += ampIndex + 1;
            }

            return false;
        }

        /// <summary>
        /// Decode string
        /// </summary>
        /// <param name="str">The string str</param>
        /// <returns></returns>
        public static string UrlDecode(string str) {
            return WebUtility.UrlDecode(str);
        }

        #endregion helpers

    }

}
