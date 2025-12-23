# SseModule [⚠️ need update to v26]

The `SseModule` 

```csharp
/// <summary>
/// Send data conformed to Server Sent Event to filtered SSE Sessions
/// </summary>
/// <param name="evt">the event name</param>
/// <param name="data">the data</param>
/// <param name="filter">filter the SSESessions (default: null)</param>
void BroadcastSSESessions(string evt, string data, Expression<Func<HttpSession, bool>> filter = null)
```

To sent reponse to all active Servent Sent Events session. A `filter` is available to selected desired session.
The `evt` and `data` parameters correspond to the format of SSE message.

