# What are Addons ?

**Addons** are optional components that extend SimpleW without modifying its core.
They allow developers to add new features, integrate services, or customize behaviors in a clear and explicit way.

Addons are designed to stay **simple, lightweight, and explicit,** avoiding complex plugin systems or hidden magic.
Each addon focuses on a specific responsibility and integrates naturally into the SimpleW pipeline.


## Services

**Service addons** provide new services that can be registered and used by SimpleW.

They usually :
- Register one or more services in the application lifecycle
- Interact with requests, responses, or the server runtime
- Expose reusable capabilities to other parts of the application

Typical use cases include authentication, logging, caching, monitoring, or background tasks.


## JsonEngines

**JsonEngine addons** add new JSON engine implementations.

They allow SimpleW to support different JSON serializers/deserializers, depending on performance needs, features, or external dependencies.

Each JsonEngine addon provides :
- A concrete implementation of a JSON engine
- A consistent API that integrates with SimpleW’s configuration

This makes it easy to switch or extend JSON handling without impacting the rest of the system.


## Helpers

**Helper addons** add utility features without introducing a full service.

They are generally :
- Stateless or lightweight
- Focused on a specific helper functionality
- Designed to simplify development or configuration

Helpers do not participate directly in the service lifecycle but provide reusable building blocks for applications and other addons.
Typical examples include utilities, helpers for request handling, configuration helpers, or small abstractions.
