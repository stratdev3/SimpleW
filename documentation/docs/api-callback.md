# Callback

The behavior of SimpleW can be overridden using a sort of callback.

## OnBeforeMethod

The `Controller` class contains an abstract method `OnBeforeMethod()` which is called before any method execution.

::: code-group

<<< @/snippets/onbefore.cs#snippet{csharp:line-numbers} [program.cs]

:::


## Subclass

A better approach for adding some logic code to all your controllers is by extending the `Controller` class.

Example using a `BaseController` class that contains common code to all controllers.

::: code-group

<<< @/snippets/subclass1.cs#snippet{csharp:line-numbers} [program.cs]

:::

The subclass can contains route's method too. To avoid this subclass being parsed by the router, it must be excluded. Use the `AddDynamicContent()` exclude parameter.

::: code-group

<<< @/snippets/subclass2.cs#snippet{csharp:line-numbers} [program.cs]

:::

Note : the method `BaseController.Conf()` with its `Route` attribute is shared across all controllers. It can be access through :
- http://localhost:2015/api/user/conf
- http://localhost:2015/api/department/conf
