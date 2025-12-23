# Module

In SimpleW, a module is a higher-level building block used to encapsulate and distribute a complete feature set.
A module can register routes, middlewares, and any related configuration in a single, reusable unit.

Modules are ideal for organizing your application and for sharing functionality as reusable packages.


## What Is a Module ?

A module is any class implementing the `IHttpModule` interface :

```csharp
public interface IHttpModule {
    void Install(SimpleWServer server);
}
```

The `Install` method is called once when the module is registered.
Inside this method, the module can:

- Register routes
- Register middlewares
- Configure server behavior
- Compose multiple concerns into a single feature

Think of a module as **a self-contained plugin for your server**.


## Registering a Module

You can register a module using the `UseModule` method :

```csharp
server.UseModule(IHttpModule module);
```

This immediately invokes the module’s `Install` method.

```csharp
public void UseModule(IHttpModule module) {
    ArgumentNullException.ThrowIfNull(module);
    module.Install(this);
}
```


## Example: Simple Module

```csharp:line-numbers
public class TestModule : IHttpModule {
    public void Install(SimpleWServer server) {
        server.MapGet("/api/test/hello", () => {
            return new { message = "Hello World !" };
        });
    }
}
```

Registering the module :

```csharp
server.UseModule(new TestModule());
```

## Cleaner API with Extensions

For a more fluent and expressive API, it is recommended to expose modules via extension methods :

```csharp:line-numbers
public static class TestModuleExtensions {
    public static SimpleWServer UseTestModule(this SimpleWServer server) {
        server.UseModule(new TestModule());
        return server;
    }
}
```

Usage :

```csharp
server.UseTestModule();
```

This approach is especially useful when distributing modules as libraries.

## Modules vs Middlewares

Middlewares are low-level pipeline components, focused on request flow.
Modules operate at a higher level and can bundle multiple middlewares, routes, and logic together.

A good rule of thumb :
- Middleware → how requests are processed
- Module → what functionality is added to the server



