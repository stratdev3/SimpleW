# Json Engine

The Json Engine defines how SimpleW serializes, deserializes, and populates objects when handling HTTP requests and responses.

It is exposed through the [`JsonEngine`](../reference/simplewserver#jsonengine) property and is used consistently by :
- Handlers returning objects
- HttpResponse.Json(...)
- Request.BodyMap(...)
- Model population and deserialization helpers

Conceptually :

> The Json Engine is the single source of truth for JSON behavior in SimpleW.


## Default Json Engine

By default, SimpleW uses `System.Text.Json`, configured with recommended, performance-oriented options.

This default engine provides :
- High performance
- Low allocations
- Native integration with .NET
- Sensible defaults for most APIs

For the majority of use cases, no configuration is required.


## Writing Your Own Json Engine

If neither `System.Text.Json` nor `Newtonsoft.Json` fits your needs, you can implement your own engine.

Simply implement the [`IJsonEngine`](../reference/ijsonengine.md) interface and register it :

```csharp
server.ConfigureJsonEngine(new MyCustomJsonEngine());
```

This makes it possible to :
- Plug in a custom serializer
- Wrap an existing JSON library
- Apply project-specific conventions


## Available Json Engine

See [Addons](../addons/addons.md) to find all the available json engines.
The most famous, `Newtonsoft`, has its [wrapper](../addons/newtonsoft.md).
