# Security

Security in a web application often starts with three simple questions:

- Who is making this request?
- How do we know that this claim is true?
- What is this caller allowed to do?

In practice, these questions map to three related but different concepts:

- **Identification** -> "I am Alice"
- **Authentication** -> "I can prove that I am Alice"
- **Authorization** -> "Alice can access this resource"

This guide gives a practical overview of those concepts so you can better understand how security is usually implemented in software applications, and how to approach it in SimpleW.

## The Core Concepts

### Identification

Identification is the **claim of identity**.

It answers:

> Who is this caller supposed to be?

Examples:

- a username
- an email address
- a user id
- a client id
- a machine name

Identification alone is **not enough** to trust the caller.
Anyone can claim to be `"admin"` if there is no proof attached to that claim.

### Authentication

Authentication is the **verification step**.

It answers:

> Can the caller prove this identity claim?

Typical authentication mechanisms:

- password verification
- API key verification
- HTTP Basic authentication
- signed bearer tokens such as JWT
- session cookies
- OpenID Connect / SSO
- client TLS certificates

Authentication usually produces a trusted application identity, often represented as a **principal** or **user context**.

### Authorization

Authorization is the **permission check** that happens after authentication.

It answers:

> Is this authenticated caller allowed to do this action?

Examples:

- user can view their own profile
- admin can access `/admin`
- finance role can download invoices
- anonymous users can access `/health`

Authorization is usually based on:

- roles
- permissions
- ownership rules
- tenant or organization membership
- feature flags or policy rules

### Authentication vs Authorization

These two concepts are often confused, but they solve different problems:

- authentication checks **who the caller is**
- authorization checks **what the caller can do**

A request can therefore be:

- **unauthenticated** -> no valid identity was established
- **authenticated but forbidden** -> identity is valid, but access is denied

In HTTP, this commonly maps to:

- `401 Unauthorized` -> the caller is not authenticated
- `403 Forbidden` -> the caller is authenticated but lacks permission

## How These Concepts Are Implemented in Software

In a typical application, security is not one single function.
It is a small pipeline.

The common flow looks like this:

1. Read identity material from the request or environment.
2. Validate it.
3. Build a trusted user object.
4. Store it in the request context.
5. Apply authorization rules before business code runs.

In concrete terms, an application usually needs:

- a **credential source**: header, cookie, token, certificate, login form
- an **identity source**: database, LDAP, external identity provider, token issuer
- a **validator**: password hash check, signature validation, certificate validation
- a **principal model**: the in-memory representation of the authenticated caller
- a **policy layer**: the rules that allow or deny access

Once the application has built a trusted principal, the rest of the code should rely on that principal instead of re-reading raw credentials everywhere.

## Security Flow

```text
Incoming request
      |
      v
Read credentials
(header, cookie, token, certificate)
      |
      v
Validate credentials
   |               |
invalid          valid
   |               |
   v               v
401 Unauthorized   Build HttpPrincipal
                       |
                       v
               Store session.Principal
                       |
                       v
        Read route metadata / authorization rules
                  |                       |
                denied                  allowed
                  |                       |
                  v                       v
            403 Forbidden               next()
```

## How Security Works in Web Applications

Web applications have an important constraint:

> HTTP is request-based and effectively stateless from the application point of view.

That means the server must decide, for each request, whether it knows the caller and whether the request is allowed.

The most common strategies are:

### Cookie-based authentication

The user logs in once.
The server issues a cookie.
Future requests send that cookie back.

The server then:

- validates the cookie or session id
- restores the user identity
- authorizes access

This model is common for:

- classic server-rendered web apps
- browser sessions
- OpenID or SSO login flows

### Token-based authentication

The client sends a token on each request, often in the `Authorization` header.

The server then:

- validates the token
- extracts user information
- restores the principal
- authorizes access

This model is common for:

- APIs
- SPAs
- mobile applications
- machine-to-machine communication

### Basic authentication

The client sends credentials on every request, usually through the `Authorization: Basic ...` header.

The server validates the credentials directly and then authorizes access.

This is simple and useful in some cases, but usually better suited for:

- internal tools
- admin endpoints
- temporary or controlled environments

### Anonymous vs Protected Routes

In a web application, not every route should have the same policy.

Typical examples:

- `/health` should often stay public
- `/login` must stay accessible before authentication
- `/me` requires an authenticated user
- `/admin` requires an authenticated user with elevated rights

This is why most web frameworks separate:

- the **authentication step** that establishes identity
- the **authorization step** that protects specific routes

## The Role of Middleware in Web Security

Middleware is usually the right place for security in a web server.

Why:

- it runs for every request
- it runs before handler business logic
- it can short-circuit early
- it can populate a request-scoped principal
- it can apply cross-cutting rules in one place

A typical security middleware does this:

1. Read credentials from the request.
2. Validate them.
3. Create the current principal.
4. Store it on the request context.
5. Check whether the matched route is public or protected.
6. Continue or return `401` / `403`.

That design keeps handlers simple:

- handlers read a trusted principal
- handlers do not re-parse tokens
- handlers do not duplicate common access rules

## How This Maps to SimpleW

SimpleW follows the same general model.

The key object is [`HttpPrincipal`](../reference/httpprincipal.md), which represents the current authenticated caller for the request.

In practice, a SimpleW security flow usually looks like this:

1. A middleware or module reads credentials from the incoming request.
2. It validates them.
3. It builds an `HttpPrincipal`.
4. It assigns it to `session.Principal`.
5. It checks route metadata and decides whether to continue.

The main pieces are:

- `HttpPrincipal` -> who the current caller is
- `HttpIdentity` -> how that caller was authenticated
- `IdentityProperty` -> custom identity data
- `session.Principal` -> request-scoped access to the current principal
- `Controller.Principal` -> controller access to the same principal
- `[AllowAnonymous]` -> declares that a handler is public
- `[RequireRole("...")]` -> declares role requirements

In SimpleW, the usual recommendation is:

- set the principal in a middleware
- check authorization in a middleware
- let handlers and controllers consume the resolved principal

See the [Principal guide](./principal.md) for the concrete SimpleW model and examples.

## Choosing an Approach in SimpleW

SimpleW supports multiple ways to implement security depending on your needs.

Common choices:

- [`SimpleW.Service.BasicAuth`](../addons/service-basicauth.md) for metadata-driven HTTP Basic protection on handlers or internal admin areas
- [`SimpleW.Service.Jwt`](../addons/service-jwt.md) for API-style bearer token authentication
- [`SimpleW.Service.OpenID`](../addons/service-openid.md) for browser login and delegated identity providers
- custom middleware when you need a fully custom policy

A useful way to think about it is:

- **BasicAuth** -> "the client sends credentials every time"
- **JWT** -> "the client sends a signed token every time"
- **OpenID** -> "the user authenticates with an external identity provider and the app restores identity from the resulting session or cookie flow"

## A Practical Mental Checklist

Before implementing security in an application, ask yourself:

1. How does the caller identify itself?
2. How is that claim authenticated?
3. Where is the trusted principal created?
4. Which routes are public?
5. Which routes require authentication?
6. Which routes require specific rights or roles?
7. What should happen on failure: `401`, `403`, or redirect?

If you can answer those questions clearly, implementing security in SimpleW becomes much easier.

## Summary

Security in a web application is usually a combination of:

- identification
- authentication
- authorization
- per-request principal restoration
- route protection

SimpleW does not invent a different model.
It gives you the building blocks to apply the same well-known security flow in a clean and explicit way.

From there, the next step is the [Principal guide](./principal.md), which shows how identity is represented and used inside a SimpleW request.
