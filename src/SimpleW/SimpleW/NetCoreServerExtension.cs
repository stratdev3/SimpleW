using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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
            return request.Header("Accept-Encoding")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
            string contentType = request.Header("Content-Type");
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
            string contentType = request.Header("Content-Type");
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

    }

}
