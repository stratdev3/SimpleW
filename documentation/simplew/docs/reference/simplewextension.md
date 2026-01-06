# SimpleWExtension

The `SimpleWExtension` static class provide several useful helpers.


## CreateJwt

```csharp
/// <summary>
/// CreateJwt
/// </summary>
/// <param name="session"></param>
/// <param name="standard"></param>
/// <param name="customClaims"></param>
/// <returns></returns>
public static string CreateJwt(this HttpSession session, JwtTokenPayload standard, IReadOnlyDictionary<string, object?> customClaims)
```


## ValidateJwt

```csharp
/// <summary>
/// ValidateJwt
/// </summary>
/// <param name="session"></param>
/// <param name="token"></param>
/// <param name="jwt"></param>
/// <param name="error"></param>
/// <returns></returns>
public static bool ValidateJwt(this HttpSession session, string token, out JwtToken? jwt, out JwtError error)
```


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
