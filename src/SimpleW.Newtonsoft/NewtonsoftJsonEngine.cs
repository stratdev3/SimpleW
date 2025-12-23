using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;


namespace SimpleW.Newtonsoft {

    /// <summary>
    /// Implement with IJsonEngine using Newtonsoft.Json
    /// </summary>
    public class NewtonsoftJsonEngine : IJsonEngine {

        /// <summary>
        /// Settings Builder
        /// </summary>
        private readonly Func<JsonAction, JsonSerializerSettings?>? SettingsBuilder;

        /// <summary>
        /// Build
        /// </summary>
        /// <returns></returns>
        private JsonSerializerSettings? Build(JsonAction action) => SettingsBuilder?.Invoke(action);

        #region cache

        /// <summary>
        /// Cache Settings for Serialize method
        /// </summary>
        private JsonSerializerSettings? SerializeSettingsCache;

        /// <summary>
        /// Cache Settings for Deserialize method
        /// </summary>
        private JsonSerializerSettings? DeserializeSettingsCache;

        /// <summary>
        /// Cache Settings for DeserializeAnonymous method
        /// </summary>
        private JsonSerializerSettings? DeserializeAnonymousSettingsCache;

        #endregion cache

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="settingsBuilder">the JsonSerializerSettings builder</param>
        public NewtonsoftJsonEngine(Func<JsonAction, JsonSerializerSettings?>? settingsBuilder = null) {
            SettingsBuilder = settingsBuilder;
            SerializeSettingsCache = Build(JsonAction.Serialize);
            DeserializeSettingsCache = Build(JsonAction.Deserialize);
            DeserializeAnonymousSettingsCache = Build(JsonAction.DeserializeAnonymous);
        }

        /// <summary>
        /// Serialize an object instance into json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        public string Serialize<T>(T value) {
            return JsonConvert.SerializeObject(value, SerializeSettingsCache);
        }

        /// <summary>
        /// Deserialize a json string into an T object instance
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public T Deserialize<T>(string json) {
            T? value = JsonConvert.DeserializeObject<T>(json, DeserializeSettingsCache);
            if (value is null) {
                throw new JsonException($"Deserialization returned null for type '{typeof(T).FullName}'. JSON might be 'null' or incompatible.");
            }
            return value;
        }

        /// <summary>
        /// Deserialize a string into an anonymous object instance
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="model"></param>
        public T DeserializeAnonymous<T>(string json, T model) {
            JsonSerializerSettings settings = DeserializeAnonymousSettingsCache ?? new JsonSerializerSettings();
            T? value = JsonConvert.DeserializeAnonymousType(json, model, settings);
            if (value is null) {
                throw new JsonException($"Deserialization returned null for anonymous type '{typeof(T).FullName}'. JSON might be 'null' or incompatible.");
            }
            return value;
        }

        /// <summary>
        /// Populate T object instance from json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="target"></param>
        /// <param name="includeProperties"></param>
        /// <param name="excludeProperties"></param>
        public void Populate<T>(string json, T target, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null) {
            if (target is null) {
                return;
            }

            JsonSerializerSettings settings = Build(JsonAction.Populate) ?? new JsonSerializerSettings();

            if (settings.ContractResolver is ShouldSerializeContractResolver resolver) {
                resolver.IncludeProperties = includeProperties;
                resolver.ExcludeProperties = excludeProperties;
            }

            JsonConvert.PopulateObject(json, target, settings);
        }

        /// <summary>
        /// Newtonsoft Recommanded Settings for SimpleW
        /// </summary>
        /// <returns></returns>
        public static Func<JsonAction, JsonSerializerSettings?> SettingsSimpleWBuilder() {
            return (action) => {
                JsonSerializerSettings settings = new();

                if (action == JsonAction.Populate) {
                    // custom contract resolver
                    settings.ContractResolver ??= new ShouldSerializeContractResolver();
                }

                // add custom jsonConverter to convert empty string to null
                settings.Converters.Add(new EmptyStringToNullConverter());

                return settings;
            };
        }

    }

    #region newtonsoft_simplew_resolver

    /// <summary>
    /// Json Custom DefaultContractResolver
    /// use to include or exclude properties
    /// from being deserialized
    /// </summary>
    public partial class ShouldSerializeContractResolver : DefaultContractResolver {

        /// <summary>
        /// Include properties
        /// </summary>
        public IEnumerable<string>? IncludeProperties { get; set; }

        /// <summary>
        /// Exclude properties
        /// </summary>
        public IEnumerable<string>? ExcludeProperties { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public ShouldSerializeContractResolver() {
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
                if (IncludeProperties != null) {
                    property.ShouldDeserialize = (instance) => { return IncludeProperties.Contains(property.PropertyName); };
                }
                if (ExcludeProperties != null) {
                    property.ShouldDeserialize = (instance) => { return !ExcludeProperties.Contains(property.PropertyName); };
                }
            }

            return property;
        }

    }

    /// <summary>
    /// Json Custom Converter: empty/whitespace string -> null (read only)
    /// </summary>
    public partial class EmptyStringToNullConverter : JsonConverter {

        /// <summary>
        /// Override ReadJson for the current StringIsEmptyToNull converter
        /// </summary>
        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) {
            if (reader.TokenType == JsonToken.Null) {
                return null;
            }

            if (reader.TokenType == JsonToken.String) {
                string? text = reader.Value?.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            // fallback: let Newtonsoft handle other token types
            return serializer.Deserialize(reader, objectType);
        }

        /// <summary>
        /// Override WriteJson
        /// </summary>
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
            throw new NotImplementedException("This converter is read-only.");
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

    #endregion newtonsoft_simplew_resolver

}