# Changelog


## Unreleased

Maintenance: compatibility with SimpleW v26.1.x.

### breakingChange

- Replace `TryAuthenticate(HttpSession session, out HttpPrincipal principal)` with `TryAuthenticate(HttpSession session, out HttpPrincipal principal, out string? error)`.



## v26.0.0 - _(2026-04-26)_

Initial release of `SimpleW.Helper.Jwt`.

### feature

- Initial `SimpleW.Helper.Jwt` package release for SimpleW v26.
- Add lightweight JWT Bearer helpers to create and validate tokens using HttpIdentity and HttpPrincipal.
