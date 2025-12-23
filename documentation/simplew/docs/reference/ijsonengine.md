# IJsonEngine

The interface is both implemented by `SystemTextJsonEngine` and `NewtonsoftJsonEngine`.
It is used to change [`SimpleWServer.JsonEngine`](./simplewserver.md#jsonengine) property.


## Signature

A `IJsonEngine` is an interface with the following method :

```csharp
/// <summary>
/// Serialize an object instance into json string
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="value"></param>
/// <returns></returns>
string Serialize<T>(T value);

```csharp
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

/// Populate T object instance from json string
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="json"></param>
/// <param name="target"></param>
/// <param name="includeProperties"></param>
/// <param name="excludeProperties"></param>
void Populate<T>(string json, T target, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null);
```
