using System.Buffers;

namespace SimpleW {

    /// <summary>
    /// Interface for JsonEngine
    /// </summary>
    public interface IJsonEngine {

        /// <summary>
        /// Serialize an object instance into json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value"></param>
        /// <returns></returns>
        string Serialize<T>(T value);

        /// <summary>
        /// Serialize an object instance into json (write directly into a IBufferWriter)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="writer"></param>
        /// <param name="value"></param>
        void SerializeUtf8<T>(IBufferWriter<byte> writer, T value);

        /// <summary>
        /// Deserialize a json string into an T object instance
        /// Contract: never returns null. Throws if JSON is "null" or cannot be deserialized to a non-null instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        T Deserialize<T>(string json);

        /// <summary>
        /// Deserialize a string into an anonymous object instance
        /// Contract: never returns null. Throws if JSON is "null" or cannot be deserialized to a non-null instance.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="model"></param>
        T DeserializeAnonymous<T>(string json, T model);

        /// <summary>
        /// Populate T object instance from json string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <param name="target"></param>
        /// <param name="includeProperties"></param>
        /// <param name="excludeProperties"></param>
        void Populate<T>(string json, T target, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null);

    }

    /// <summary>
    /// Json Action
    /// </summary>
    public enum JsonAction {
        /// <summary>
        /// Serialize
        /// </summary>
        Serialize,
        /// <summary>
        /// Deserialize
        /// </summary>
        Deserialize,
        /// <summary>
        /// DeserializeAnonymous
        /// </summary>
        DeserializeAnonymous,
        /// <summary>
        /// Populate
        /// </summary>
        Populate
    }

}