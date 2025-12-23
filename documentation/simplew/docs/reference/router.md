# Router

The primary goal of the `Router` is to execute `delegate` depending on [`Method`](./httprequest#method) and [`Url`](./httprequest#url).


## Routes

```csharp
/// <summary>
/// Public Property List of all declared and handle Routes
/// </summary>
public List<Route> Routes { get; private set; }
```