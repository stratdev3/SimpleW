using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private readonly Func<string, JsonSerializerSettings> SettingsBuilder;

        /// <summary>
        /// Build
        /// </summary>
        /// <returns></returns>
        private JsonSerializerSettings Build(string action) => SettingsBuilder == null ? null : SettingsBuilder(action);

        #region cache

        /// <summary>
        /// Cache Settings for Serialize method
        /// </summary>
        private JsonSerializerSettings SerializeSettingsCache;

        /// <summary>
        /// Cache Settings for Deserialize method
        /// </summary>
        private JsonSerializerSettings DeserializeSettingsCache;

        /// <summary>
        /// Cache Settings for DeserializeAnonymous method
        /// </summary>
        private JsonSerializerSettings DeserializeAnonymousSettingsCache;

        #endregion cache

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="settingsBuilder">the JsonSerializerSettings builder</param>
        public NewtonsoftJsonEngine(Func<string, JsonSerializerSettings> settingsBuilder = null) {
            SettingsBuilder = settingsBuilder;
            SerializeSettingsCache = Build(nameof(Serialize));
            DeserializeSettingsCache = Build(nameof(Deserialize));
            DeserializeAnonymousSettingsCache = Build(nameof(DeserializeAnonymous));
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
            return JsonConvert.DeserializeObject<T>(json, DeserializeSettingsCache);
        }

        /// <summary>
        /// Deserialize a string into an anonymous object instance
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="model"></param>
        public T DeserializeAnonymous<T>(string json, T model) {
            return JsonConvert.DeserializeAnonymousType(json, model, DeserializeAnonymousSettingsCache);
        }

        /// <summary>
        /// Populate T object instance from json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="target"></param>
        /// <param name="includeProperties"></param>
        /// <param name="excludeProperties"></param>
        public void Populate<T>(string json, T target, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null) {

            JsonSerializerSettings settings = Build(nameof(Populate));

            if (settings != null && settings.ContractResolver is ShouldSerializeContractResolver resolver) {
                resolver.IncludeProperties = includeProperties;
                resolver.ExcludeProperties = excludeProperties;
            }
            JsonConvert.PopulateObject(json, target, settings);
        }

        /// <summary>
        /// Newtonsoft Recommanded Settings for SimpleW
        /// </summary>
        /// <returns></returns>
        public static Func<string, JsonSerializerSettings> SettingsSimpleWBuilder() {
            return (action) => {
                JsonSerializerSettings settings = new();

                if (action == nameof(Populate)) {
                    // custom contract resolver
                    settings.ContractResolver ??= new ShouldSerializeContractResolver();
                }

                // add custom jsonConverter to convert empty string to null
                settings.Converters.Add(new EmptyStringToNullConverter());
                // add custom jsonConverter to convert hh:mm to timeonly
                settings.Converters.Add(new TimeOnlyHHmmConverter());

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
        public IEnumerable<string> IncludeProperties { get; set; }

        /// <summary>
        /// Exclude properties
        /// </summary>
        public IEnumerable<string> ExcludeProperties { get; set; }

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

    #endregion newtonsoft_simplew_resolver

}