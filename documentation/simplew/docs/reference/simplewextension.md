# SimpleWExtension

The `SimpleWExtension` static class provide several useful helpers.


## JsonMap()

```csharp
/// <summary>
/// Update the model with data from POST
/// </summary>
/// <param name="json">The json string.</param>
/// <param name="model">The Model instance to populate.</param>
/// <param name="jsonEngine">the json library to handle serialization/deserialization (default: JsonEngine)</param>
/// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
/// <param name="excludeProperties">string array of properties to not update.</param>
/// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
public static bool JsonMap<TModel>(string json, TModel model, IJsonEngine jsonEngine, IEnumerable<string>? includeProperties = null, IEnumerable<string>? excludeProperties = null)
```
