# Router

The primary goal of the `Router` is to execute a `delegate` based on the [`Method`](./httprequest#method) and [`Url`](./httprequest#rawtarget).


## Routes

```csharp
/// <summary>
/// All declared Routes
/// </summary>
public IEnumerable<RouteInfo> Routes { get; }
```

See more [examples](../guide/routing.md).