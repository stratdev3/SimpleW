# CorsModule

The `CorsModule` 

```csharp
/// <summary>
/// Setup CORS
/// </summary>
/// <param name="origin">Access-Control-Allow-Origin</param>
/// <param name="headers">Access-Control-Allow-Headers</param>
/// <param name="methods">Access-Control-Allow-Methods</param>
/// <param name="credentials">Access-Control-Allow-Credentials</param>
void AddCORS(string origin="*", string headers = "*", string methods = "GET,POST,OPTIONS", string credentials="true")
```

Setup the Cross-Origin Resource Sharing policy and so, add 4 headers to every response.
`server.AddCORS()` method should be called before any `server.AddStaticContent()`.
