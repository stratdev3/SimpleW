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


## Using Newtonsoft.Json

Some projects require features or behaviors that are specific to [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) (custom converters, advanced polymorphism, legacy compatibility, etc.).

For this reason, SimpleW provides an official alternative engine via the [SimpleW.JsonEngine.Newtonsoft](https://www.nuget.org/packages/SimpleW.JsonEngine.Newtonsoft) package.

#### Installation

```sh
$ dotnet add package SimpleW.JsonEngine.Newtonsoft
```


## Configuring the Json Engine

### Basic Configuration

To replace the default engine with Newtonsoft.Json :

```csharp
server.ConfigureJsonEngine(new NewtonsoftJsonEngine());
```

This applies globally to :
- The server
- All controllers
- All handlers

### Custom Newtonsoft Settings

You can also customize the `JsonSerializerSettings` used by Newtonsoft.Json.

```csharp
server.ConfigureJsonEngine(new NewtonsoftJsonEngine(
    (action) => {
        Newtonsoft.Json.JsonSerializerSettings settings = new();

        // you can customize settings dependings the IJsonEngine method called
        if (action == JsonAction.Serialize) {
            settings.Formatting = Newtonsoft.Json.Formatting.Indented;
        }

        return settings;
    }
));
```

This allows fine-grained control depending on the operation being performed (e.g. serialization vs deserialization).


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

