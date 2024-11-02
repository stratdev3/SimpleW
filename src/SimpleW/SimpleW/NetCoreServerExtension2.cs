using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using System.Threading;
using NetCoreServer;


namespace SimpleW {

    /// <summary>
    /// NetCoreServer Extension helpers methods Native DOTNET
    /// </summary>
    public static partial class NetCoreServerExtension {

        #region parse

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <param name="options">JsonSerializerOptions for the PopulateObject() method.</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMapNative<TModel>(this HttpRequest request, TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, JsonSerializerOptions options = null) {
            string contentType = request.Header("Content-Type");
            string body = request.Body;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new Exception($"multipart/form-data contentType must be parsed with {nameof(BodyFile)}() method");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, object> kv = BodyForm(body);
                body = JsonSerializer.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {
                return JsonMapNative(body, model, includeProperties, excludeProperties, options);
            }

            return true;
        }

        /// <summary>
        /// Update the anonymous model with data from POST
        /// </summary>
        /// <param name="request">The HttpRequest request.</param>
        /// <param name="model">The Anonymous Model instance to populate.</param>
        /// <param name="options">JsonSerializerOptions for the PopulateObject() method.</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool BodyMapAnonymousNative<TModel>(this HttpRequest request, ref TModel model, JsonSerializerOptions options = null) {
            string contentType = request.Header("Content-Type");
            string body = request.Body;

            if (string.IsNullOrWhiteSpace(body)) {
                return false;
            }

            // if uploading data from html from multipart/form-data
            if (contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) {
                throw new Exception($"multipart/form-data contentType must be parsed with {nameof(BodyFile)}() method");
            }

            // if html form, convert to json string
            if (contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) {
                Dictionary<string, object> kv = BodyForm(body);
                body = JsonSerializer.Serialize(kv);
                contentType = "application/json";
            }

            if (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) {

                // create options is null
                if (options == null) {
                    options = new JsonSerializerOptions();
                }

                // deserialize AnonymousType
                model = JsonSerializerExtension.DeserializeAnonymousType(body, model);
            }

            return true;
        }

        #region serializer_net7

        /// <summary>
        /// Update the model with data from POST
        /// </summary>
        /// <param name="json">The json string.</param>
        /// <param name="model">The Model instance to populate.</param>
        /// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
        /// <param name="excludeProperties">string array of properties to not update.</param>
        /// <param name="options">JsonSerializerOptions for the JsonConvert.PopulateObject() method.</param>
        /// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
        public static bool JsonMapNative<TModel>(string json, TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, JsonSerializerOptions options = null) {

            if (string.IsNullOrWhiteSpace(json)) {
                return false;
            }

            // create options is null
            if (options == null) {
                options = new JsonSerializerOptions();
            }

            // add custom contractResolver to include or exclude properties
            if (options.TypeInfoResolver == null
                && (includeProperties != null || excludeProperties != null)
            ) {
                ShouldSerializePropertiesWithName modifier = new(includeProperties, excludeProperties);
                options.TypeInfoResolver = new DefaultJsonTypeInfoResolver {
                    Modifiers = { modifier.ModifyTypeInfo }
                };
            }

            // deserialize
            JsonSerializerExtension.PopulateObject(json, model, options);

            return true;
        }

        /// <summary>
        /// Json Custom DefaultContractResolver
        /// use to include or exclude properties
        /// from being deserialized
        /// Source : https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/custom-contracts#example-ignore-properties-with-a-specific-type
        /// </summary>
        public class ShouldSerializePropertiesWithName {

            private readonly IEnumerable<string> _includeProperties;
            private readonly IEnumerable<string> _excludeProperties;

            /// <summary>
            /// Set include properties
            /// </summary>
            /// <param name="includeProperties"></param>
            public ShouldSerializePropertiesWithName(IEnumerable<string> includeProperties) {
                _includeProperties = includeProperties;
            }
            /// <summary>
            /// Set include and exclude properties
            /// </summary>
            /// <param name="includeProperties"></param>
            /// <param name="excludeProperties"></param>
            public ShouldSerializePropertiesWithName(IEnumerable<string> includeProperties, IEnumerable<string> excludeProperties) {
                _includeProperties = includeProperties;
                _excludeProperties = excludeProperties;
            }

            /// <summary>
            /// Flag each public property to allow/deny deserialization
            /// </summary>
            /// <returns></returns>
            public void ModifyTypeInfo(JsonTypeInfo ti) {
                if (ti.Kind != JsonTypeInfoKind.Object) {
                    return;
                }

                ti.Properties.RemoveAll(prop => (_includeProperties != null && !_includeProperties.Contains(prop.Name))
                                                || (_excludeProperties != null && _excludeProperties.Contains(prop.Name)));
            }
        }

        #endregion serializer_net7

        #endregion parse

    }


    public static class ListHelpers {
        // IList<T> implementation of List<T>.RemoveAll method.
        public static void RemoveAll<T>(this IList<T> list, Predicate<T> predicate) {
            for (int i = 0; i < list.Count; i++) {
                if (predicate(list[i])) {
                    list.RemoveAt(i--);
                }
            }
        }
    }


    #region net7workaround

    /// <summary>
    /// source: https://stackoverflow.com/a/65433372/3699737
    /// </summary>
    public static partial class JsonSerializerExtension {
        public static T? DeserializeAnonymousType<T>(string json, T anonymousTypeObject, JsonSerializerOptions? options = default)
            => JsonSerializer.Deserialize<T>(json, options);

        public static ValueTask<TValue?> DeserializeAnonymousTypeAsync<TValue>(Stream stream, TValue anonymousTypeObject, JsonSerializerOptions? options = default, CancellationToken cancellationToken = default)
            => JsonSerializer.DeserializeAsync<TValue>(stream, options, cancellationToken); // Method to deserialize from a stream added for completeness
    }


    /// <summary>
    /// WORKAROUND 
    /// source: https://github.com/dotnet/runtime/issues/29538
    /// workaround code: https://github.com/dotnet/runtime/issues/29538#issuecomment-1330494636
    /// workaround code 2 : https://github.com/microsoftgraph/msgraph-sdk-dotnet-core/blob/57861dc4aea6c33908838915c97fc02105b6e788/src/Microsoft.Graph.Core/Serialization/DerivedTypeConverter.cs#L112-L114
    /// FUTUR : https://github.com/dotnet/runtime/issues/78556#issuecomment-1331932270
    /// </summary>
    public static partial class JsonSerializerExtension {

        // Dynamically attach a JsonSerializerOptions copy that is configured using PopulateTypeInfoResolver
        private readonly static ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> s_populateMap = new();

        public static void PopulateObject<T>(string json, T destination, JsonSerializerOptions? options = null) {
            options = GetOptionsWithPopulateResolver(options);
            //Debug.Assert(options.TypeInfoResolver is PopulateTypeInfoResolver);
            PopulateTypeInfoResolver.t_populateObject = destination;
            try {
                T? result = JsonSerializer.Deserialize<T>(json, options);
                //Debug.Assert(ReferenceEquals(result, destination));
            }
            finally {
                PopulateTypeInfoResolver.t_populateObject = null;
            }
        }
        public static void PopulateObject(string json, Type returnType, object destination, JsonSerializerOptions? options = null) {
            options = GetOptionsWithPopulateResolver(options);
            PopulateTypeInfoResolver.t_populateObject = destination;
            try {
                object? result = JsonSerializer.Deserialize(json, returnType, options);
                //Debug.Assert(ReferenceEquals(result, destination));
            }
            finally {
                PopulateTypeInfoResolver.t_populateObject = null;
            }
        }

        private static JsonSerializerOptions GetOptionsWithPopulateResolver(JsonSerializerOptions? options) {
            options ??= JsonSerializerOptions.Default;

            if (!s_populateMap.TryGetValue(options, out JsonSerializerOptions? populateResolverOptions)) {
                JsonSerializer.Serialize(value: 0, options); // Force a serialization to mark options as read-only
                //Debug.Assert(options.TypeInfoResolver != null);

                populateResolverOptions = new JsonSerializerOptions(options) {
                    TypeInfoResolver = new PopulateTypeInfoResolver(options.TypeInfoResolver)
                };

                s_populateMap.TryAdd(options, populateResolverOptions);
            }

            return populateResolverOptions;
        }

        private class PopulateTypeInfoResolver : IJsonTypeInfoResolver {
            private readonly IJsonTypeInfoResolver _jsonTypeInfoResolver;
            [ThreadStatic] internal static object? t_populateObject;

            public PopulateTypeInfoResolver(IJsonTypeInfoResolver jsonTypeInfoResolver) {
                _jsonTypeInfoResolver = jsonTypeInfoResolver;
            }

            public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options) {
                JsonTypeInfo typeInfo = _jsonTypeInfoResolver.GetTypeInfo(type, options);
                if (typeInfo != null && typeInfo.Kind != JsonTypeInfoKind.None) {
                    Func<object>? defaultCreateObjectDelegate = typeInfo.CreateObject;
                    typeInfo.CreateObject = () =>
                    {
                        object? result = t_populateObject;
                        if (result != null) {
                            // clean up to prevent reuse in recursive scenaria
                            t_populateObject = null;
                        }
                        else {
                            // fall back to the default delegate
                            result = defaultCreateObjectDelegate?.Invoke();
                        }
                        return result!;
                    };
                }

                return typeInfo;
            }
        }
    }

    #endregion net7workaround

}
