# Newtonsoft

[`SimpleW.JsonEngine.Newtonsoft`](https://www.nuget.org/packages/SimpleW.JsonEngine.Newtonsoft) is an optional integration package that allows you to run a **SimpleW server** using the **Microsoft.Extensions.Hosting** infrastructure.


## Using Newtonsoft.Json

Some projects require features or behaviors that are specific to [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json) (custom converters, advanced polymorphism, legacy compatibility, etc.).

For this reason, SimpleW provides an official alternative engine via the `SimpleW.JsonEngine.Newtonsoft` package.

#### Installation

```sh
$ dotnet add package SimpleW.JsonEngine.Newtonsoft --version 26.0.0-beta.20260221-1486
```


## Configuring the Json Engine

### Basic Configuration

To replace the default engine with Newtonsoft.Json :

```csharp
using SimpleW.JsonEngine.Newtonsoft;
//...
server.ConfigureJsonEngine(new NewtonsoftJsonEngine());
```

This applies globally to :
- The server
- All controllers
- All handlers

### Custom Newtonsoft Settings

You can also customize the `JsonSerializerSettings` used by Newtonsoft.Json.

```csharp
using SimpleW.JsonEngine.Newtonsoft;
//...
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

