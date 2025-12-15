# NetCoreServerExtension

The `NetCoreServerExtension` static class bring many useful helpers.


## CreateJwt()

They are multiples methods `CreateJwt()` to forge a token

```csharp
/// <summary>
/// Create a JWT token
/// </summary>
/// <param name="payload">The Dictionary payload</param>
/// <param name="key">The string secret key from which the token is sign</param>
/// <param name="issuer">The string issuer which is allowed</param>
/// <param name="expiration">The int expiration time in second (default: 15 minutes)</param>
/// <returns>The token string</returns>
string CreateJwt(Dictionary<string, object> payload, string key, string issuer = null, double expiration = 15*60)
```

```csharp
/// <summary>
/// Create a JWT token
/// </summary>
/// <param name="payload">The Dictionary payload</param>
/// <param name="key">The string secret key from which the token is sign</param>
/// <param name="expiration">The datetime expiration</param>
/// <param name="issuer">The string issuer which is allowed</param>
/// <returns>The token string</returns>
string CreateJwt(Dictionary<string, object> payload, string key, DateTime expiration, string issuer = null)
```

```csharp
/// <summary>
/// Create a JWT token
/// </summary>
/// <param name="webuser">The IWebUser</param>
/// <param name="key">The string secret key from which the token is sign</param>
/// <param name="issuer">The string issuer which is allowed</param>
/// <param name="expiration">The int expiration time in second (default: 15 minutes)</param>
/// <param name="refresh">The bool refresh</param>
/// <returns>The token string</returns>
string CreateJwt(IWebUser webuser, string key, string issuer = null, double expiration = 15 * 60, bool refresh = true)
```

```csharp
/// <summary>
/// Create a JWT token
/// </summary>
/// <param name="webuser">The IWebUser</param>
/// <param name="key">The string secret key from which the token is sign</param>
/// <param name="issuer">The string issuer which is allowed</param>
/// <param name="expiration">The datetime expiration</param>
/// <param name="refresh">The bool refresh</param>
/// <returns>The token string</returns>
string CreateJwt(IWebUser webuser, string key, DateTime expiration, string issuer = null,bool refresh = true)
```

## ValidateJwt()

```csharp
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
public static T ValidateJwt<T>(this string token, string key, string issuer = null)
```

The `ValidateJwt<T>()` string extension method allows verify a token from a string.


## JsonMap()

```csharp
/// <summary>
/// Update the model with data from POST
/// </summary>
/// <param name="json">The json string.</param>
/// <param name="model">The Model instance to populate.</param>
/// <param name="includeProperties">string array of properties to update the model. if null update all.</param>
/// <param name="excludeProperties">string array of properties to not update.</param>
/// <param name="jsonEngine">the json library to handle serialization/deserialization (default: JsonEngine)</param>
/// <returns><c>true</c> if operation success; otherwise, <c>false</c>.</returns>
public static bool JsonMap<TModel>(string json, TModel model, IEnumerable<string> includeProperties = null, IEnumerable<string> excludeProperties = null, IJsonEngine jsonEngine = null)
```
