using System;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using HttpMultipartParser;
using LitJWT;
using LitJWT.Algorithms;
using NetCoreServer;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;


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
            for (var i = 0; i < request.Headers; i++) {
                (string headerName, string headerValue) = request.Header(i);
                if (string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase)) {
                    return headerValue;
                }
            }
            return null;
        }

        #endregion properties


        #region parse

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <param name="settings">JsonSerializerSettings for the JsonConvert.PopulateObject() method.</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMap<TModel>(this HttpRequest request, TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, JsonSerializerSettings settings = null) {
            var contentType = request.Header("Content-Type");
            var body = request.Body;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new Exception($"multipart/form-data contentType must be parsed with {nameof(BodyFile)}() method");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                var kv = BodyForm(body);
                body = System.Text.Json.JsonSerializer.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {
                return JsonMap(body, model, includeProperties, excludeProperties, settings);
            }

            return true;
        }

        /// <summary>
        /// Update the anonymous model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Anonymous Model instance to populate.</param>
        /// <param name="settings">JsonSerializerSettings for the JsonConvert.PopulateObject() method.</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMapAnonymous<TModel>(this HttpRequest request, ref TModel model, JsonSerializerSettings settings = null) {
            var contentType = request.Header("Content-Type");
            var body = request.Body;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new Exception($"multipart/form-data contentType must be parsed with {nameof(BodyFile)}() method");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                var kv = BodyForm(body);
                body = System.Text.Json.JsonSerializer.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {

                // create settings is null
                if (settings == null) {
                    settings = new JsonSerializerSettings();
                }

                // add custom jsonConverter to convert empty string to null
                settings.Converters.Add(new EmptyStringToNullConverter());
                // add custom jsonConverter to convert hh:mm to timeonly
                settings.Converters.Add(new TimeOnlyHHmmConverter());

                // deserialize AnonymousType
                model = JsonConvert.DeserializeAnonymousType(body, model);
            }

            return true;
        }

        /// <summary>
        /// Parses multipart/form-data and contentType given the request body (Request.InputStream)
        /// Please note the underlying input stream is not rewindable.
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <returns>MultipartFormDataParser</returns>
        public static MultipartFormDataParser BodyFile(this HttpRequest request) {
            var stream = new MemoryStream(request.BodyBytes);
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
            var kvpSeparator = new[] { '=' };

            // Create the result object
            var resultDictionary = new Dictionary<string, object>();

            // Split the request body into key-value pair strings
            var keyValuePairStrings = requestBody.Split('&');

            foreach (var kvps in keyValuePairStrings) {
                // Skip KVP strings if they are empty
                if (string.IsNullOrWhiteSpace(kvps)) {
                    continue;
                }

                // Split by the equals char into key values.
                // Some KVPS will have only their key, some will have both key and value
                // Some other might be repeated which really means an array
                var kvpsParts = kvps.Split(kvpSeparator, 2);

                // We don't want empty KVPs
                if (kvpsParts.Length == 0) {
                    continue;
                }

                // Decode the key and the value. Discard Special Characters
                var key = WebUtility.UrlDecode(kvpsParts[0]);
                if (key.IndexOf("[]", StringComparison.OrdinalIgnoreCase) > 0) {
                    key = key[..key.IndexOf("[]", StringComparison.OrdinalIgnoreCase)];
                }

                var value = kvpsParts.Length >= 2 ? WebUtility.UrlDecode(kvpsParts[1]) : null;

                // If the result already contains the key, then turn the value of that key into a List of strings
                if (resultDictionary.ContainsKey(key)) {
                    // Check if this key has a List value already
                    if (resultDictionary[key] is not List<string> listValue) {
                        // if we don't have a list value for this key, then create one and add the existing item
                        var existingValue = resultDictionary[key] as string;
                        resultDictionary[key] = new List<string>();
                        listValue = (List<string>)resultDictionary[key];
                        listValue.Add(existingValue);
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


        #region serializer_Newtonsoft

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="json">The json string.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <param name="settings">JsonSerializerSettings for the JsonConvert.PopulateObject() method.</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool JsonMap<TModel>(string json, TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, JsonSerializerSettings settings = null) {

            if (string.IsNullOrWhiteSpace(json)) {
                return false;
            }

            // create settings is null
            if (settings == null) {
                settings = new JsonSerializerSettings();
            }

            // add custom contractResolver to include or exclude properties
            if (settings.ContractResolver == null
                && (includeProperties != null || excludeProperties != null)
            ) {
                settings.ContractResolver = new ShouldSerializeContractResolver(includeProperties, excludeProperties);
                //settings.ContractResolver = new UpcastingContractResolver<Element, IElementWriter>();
            }

            // add custom jsonConverter to convert empty string to null
            settings.Converters.Add(new EmptyStringToNullConverter());
            // add custom jsonConverter to convert hh:mm to timeonly
            settings.Converters.Add(new TimeOnlyHHmmConverter());

            // deserialize
            JsonConvert.PopulateObject(json, model, settings);

            return true;
        }

        /// <summary>
        /// Json Custom DefaultContractResolver
        /// use to include or exclude properties
        /// from being deserialized
        /// </summary>
        public partial class ShouldSerializeContractResolver : DefaultContractResolver {

            private readonly IEnumerable<string> _includeProperties;
            private readonly IEnumerable<string> _excludeProperties;

            /// <summary>
            /// Set include properties
            /// </summary>
            /// <param name="includeProperties"></param>
            public ShouldSerializeContractResolver(IEnumerable<string> includeProperties) {
                _includeProperties = includeProperties;
            }
            /// <summary>
            /// Set include and exclude properties
            /// </summary>
            /// <param name="includeProperties"></param>
            /// <param name="excludeProperties"></param>
            public ShouldSerializeContractResolver(IEnumerable<string> includeProperties, IEnumerable<string> excludeProperties) {
                _includeProperties = includeProperties;
                _excludeProperties = excludeProperties;
            }

            /// <summary>
            /// Flag each public property to allow/deny deserialization
            /// </summary>
            /// <param name="member"></param>
            /// <param name="memberSerialization"></param>
            /// <returns></returns>
            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
                JsonProperty property = base.CreateProperty(member, memberSerialization);

                if (member is PropertyInfo propertyInfo) {
                    if (_includeProperties != null) {
                        property.ShouldDeserialize = (instance) => { return _includeProperties.Contains(property.PropertyName); };
                    }
                    if (_excludeProperties != null) {
                        property.ShouldDeserialize = (instance) => { return !_excludeProperties.Contains(property.PropertyName); };
                    }
                }

                return property;
            }

        }

        /// <summary>
        /// Json Custom JsonConverter
        /// use to convert empty string to null
        /// and so avoid unecessary pendingchangestrings
        /// when deserialized
        /// </summary>
        public partial class EmptyStringToNullConverter : JsonConverter {

            /// <summary>
            /// Override ReadJson for the current StringIsEmptyToNull converter
            /// </summary>
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                if (reader.Value == null) {
                    return null;
                }
                string text = reader.Value.ToString();
                if (string.IsNullOrWhiteSpace(text)) {
                    return null;
                }
                return text;
            }

            /// <summary>
            /// Override WriteJson
            /// </summary>
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                throw new NotImplementedException("Not needed because this converter cannot write json");
            }

            /// <summary>
            /// Override CanWrite
            /// </summary>
            public override bool CanWrite {
                get { return false; }
            }

            /// <summary>
            /// Override CanConvert
            /// </summary>
            public override bool CanConvert(Type objectType) {
                return objectType == typeof(string);
            }

        }

        /// <summary>
        /// Json Custom JsonConverter
        /// use to convert TimeOnly from "HH:mm"
        /// and fix the https://github.com/JamesNK/Newtonsoft.Json/issues/2810
        /// waiting the release
        /// </summary>
        public partial class TimeOnlyHHmmConverter : JsonConverter {

            /// <summary>
            /// Override ReadJson for the current TimeOnly converter
            /// </summary>
            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                if (reader.Value == null) {
                    return null;
                }
                TimeOnly time = TimeOnly.Parse(reader.Value.ToString(), CultureInfo.InvariantCulture);
                return time;
            }

            /// <summary>
            /// Override WriteJson
            /// </summary>
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                throw new NotImplementedException("Not needed because this converter cannot write json");
            }

            /// <summary>
            /// Override CanWrite
            /// </summary>
            public override bool CanWrite {
                get { return false; }
            }

            /// <summary>
            /// Override CanConvert
            /// </summary>
            public override bool CanConvert(Type objectType) {
                return objectType == typeof(TimeOnly) || objectType == typeof(TimeOnly?);
            }

        }

        #endregion serializer_Newtonsoft

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

            var decoder = new JwtDecoder(new HS256Algorithm(Encoding.UTF8.GetBytes(key)));
            try {
                // TODO : check issuer
                var result = decoder.TryDecode(token, x => JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(x)), out var t);
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

            var encoder = new JwtEncoder(new HS256Algorithm(Encoding.UTF8.GetBytes(key)));

            var token = encoder.Encode(
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

            var encoder = new JwtEncoder(new HS256Algorithm(Encoding.UTF8.GetBytes(key)));

            var token = encoder.Encode(
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
            var payload = new Dictionary<string, object>() {
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
            var payload = new Dictionary<string, object>() {
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
