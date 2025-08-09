# Router

The primary goal of the `Router` is to call component depending on [`Method`](./controller-httprequest#method) and [`Url`](./controller-httprequest#url).


## RegExpEnabled

```csharp
/// <summary>
/// Enable Regular Expression for Route.Path
/// Consider RegExpEnabled to be slower
/// scope : global to all AddDynamicContent()
/// </summary>
public bool RegExpEnabled { get; set; } = false;
```


## Routes

```csharp
/// <summary>
/// Public Property List of all declared and handle Routes
/// </summary>
public List<Route> Routes { get; private set; }
```