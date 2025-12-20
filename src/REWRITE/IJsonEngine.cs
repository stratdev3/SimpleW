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
        /// Deserialize a json string into an T object instance
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        T Deserialize<T>(string json);

        /// <summary>
        /// Deserialize a string into an anonymous object instance
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

}