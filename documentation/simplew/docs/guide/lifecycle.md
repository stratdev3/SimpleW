# Lifecycle

How is it working before and after server started.


## Before Server Start

Each use of a component [`AddDynamicContent()`](../reference/simplewserver#adddynamiccontent), [`AddStaticContent()`](../reference/simplewserver#addstaticcontent) (...) will inspect for the looking classes or files and their location be added to the `Router`.

It's run once then everything is cached : statics files or method with compiled delegate.


## After Server Start

The client requests will throught the following process :

1. `SimpleWServer` or `SimpleWServer` class handle all the connection and create a `HttpRequest`
2. The `HttpRequest` is passed to the `Router` which will compare leverage the corresponding modules (`Dynamic`, `Static`, `Websocket`...) according to Url and Method.
3. The module will then find the item :
    1. for `Dynamic` module :
        - look for the corresponding method to call
        - instanciate underlying class of the method and inject the `HttpRequest` to `Request` property on the fly
        - execute the method and send a response to the client containing the serialized result of method
    2. for `Static` module :
        - look for the corresponding file in the `Cache`. If not, get the file on the disk and put it on `Cache`.
        - send a download response to the client.
    3. for `WebSocket` module :
        - look for the corresponding method to call
        - instanciate underlying class of the method and inject the `HttpRequest` to `Request` property on the fly
        - execute the method and send a response to the client containing the serialized result of method
    4. for `ServerSentEvent` module :
        - look for the corresponding method to call
        - instanciate underlying class of the method and inject the `HttpRequest` to `Request` property on the fly
        - execute the method and send a response to the client containing the serialized result of method
4. Close the connection to the client unless a module keep it alive.