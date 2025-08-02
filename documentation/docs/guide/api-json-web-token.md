# JWT Authentication


[JSON Web Tokens](https://jwt.io/) are an open, industry standard [RFC 7519](https://tools.ietf.org/html/rfc7519) method for representing claims securely between two parties. 
SimpleW internal use the [LitJWT](https://github.com/Cysharp/LitJWT) project to forge and verify json web token.

## Get Token

The `Controller.GetJwt()` can be used to get the raw JWT string sent by a client.

Backend receive

::: code-group

<<< @/snippets/jwt-get.cs#snippet{csharp:line-numbers} [program.cs]

:::

Frontend send with JWT as a classic `Bearer Authorisation` Header

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c" \
     "http://localhost:2015/api/test/token"
```

Frontend send with JWT as `jwt` query string

```bash
curl -H "Authorization: Bearer " \
     "http://localhost:2015/api/test/token?jwt=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
```

### Notes

There is no need to declare specific parameter in the Controller.

The `GetJwt()` will internally parse the client request looking for, by order of appearance :
1. `Session.jwt` (websocket only)
2. `jwt` querystring in the request url (api only)
3. `Authorization: bearer` in the request header (api only)

### Why different ways for passing jwt ?

Passing jwt in the `Header` __should always__ be the preferred method.

But sometimes, header cannot be modified by client and passing jwt in the url is the only way. Example : internet browser trying to render image from `<img src= />` without javascript.

In this case, try to forge a specific JWT with role based access limited to the target ressource only and a very short period expiration (see next chapter to [forge jwt](#forge-jwt)).

### Override GetJwt()

You can provide your own implementation of the `GetJwt()` by overriding in a [subclass](#subclass).

Example of overriding

::: code-group

<<< @/snippets/jwt-get-override.cs#snippet{csharp:line-numbers} [program.cs]

:::


## Verify Token

The `ValidateJwt<T>()` string extension can be used to verify a json token.

Frontend with a jwt (forge with "secret" as secret, see [jwt.io](https://jwt.io) for details)

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiaWQiOiJiODRjMDM5Yy0zY2QyLTRlN2ItODEyYy05MTQxZWQ2YzU2ZTQiLCJuYW1lIjoiSm9obiBEb2UiLCJyb2xlcyI6WyJhY2NvdW50Il0sImlhdCI6MjUxNjIzOTAyMn0.QhJ1EiMIt4uAGmYrGAC53PxoHIfX6aiWiLRbhastoB4" \
     "http://localhost:2015/api/user/account"
```

Backend receive

::: code-group

<<< @/snippets/jwt-verify.cs#snippet{csharp:line-numbers} [program.cs]

:::

The `ValidateJwt<UserToken>()` will verify token and convert payload into a `UserToken` instance.
Then, you can use `userToken` to check according to your business rules.


### Refactor the JWT verification logic

This example shows how to integrate a global custom jwt verification in all controllers using [subclass](#subclass) and [hooks](#hooks)

Frontend send

```bash
curl -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiaWQiOiJiODRjMDM5Yy0zY2QyLTRlN2ItODEyYy05MTQxZWQ2YzU2ZTQiLCJuYW1lIjoiSm9obiBEb2UiLCJyb2xlcyI6WyJhY2NvdW50Il0sImlhdCI6MjUxNjIzOTAyMn0.QhJ1EiMIt4uAGmYrGAC53PxoHIfX6aiWiLRbhastoB4" \
     "http://localhost:2015/api/user/account"
```

Backend receive

::: code-group

<<< @/snippets/jwt-verify-full.cs#snippet{csharp:line-numbers} [program.cs]

:::

## Forge Token

The `NetCoreServerExtension.CreateJwt()` method can be used to forge a json token which will be [Validate](#verify-jwt) later.


```bash
curl "http://localhost:2015/api/test/forge"
```

Backend receive

::: code-group

<<< @/snippets/jwt-forge.cs#snippet{csharp:line-numbers} [program.cs]

:::

Note: Just browse `NetCoreServerExtension.CreateJwt()` to discover all parameters.
