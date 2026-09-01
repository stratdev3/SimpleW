# Changelog


## v26.1.0 - _(2026-08-29)_

Maintenance: compatibility with SimpleW v26.1.x.

### fix

- Handle `ListenerReloadException` during certificate-driven listener reloads, including rollback failures.

### breakingChange

- Remove `LetsEncryptOptions.SslContextFactory`. Use `OnEngineHttpsEnable` and `OnEngineHttpsDisable` to configure TLS on the selected engine; their default implementations support `SimpleWEngine`.



## v26.0.0 - _(2026-04-26)_

Initial release of `SimpleW.Service.LetsEncrypt`.

### feature

- Initial `SimpleW.Service.LetsEncrypt` package release for SimpleW v26.
- Add Let's Encrypt ACME support for automatic HTTPS certificate provisioning and renewal.
