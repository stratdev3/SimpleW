# SSL Certificate

The [HTTPS](https://en.wikipedia.org/wiki/HTTPS) protocol is supported and you can bring your own certificate in `PKCS#12` format.

With just a small change, the [basic static example](./static-files) can serve HTTPS.

::: code-group

<<< @/snippets/ssl-certificate.cs#snippet{16-20,23,26 csharp:line-numbers} [program.cs]

:::

::: tip NOTE
The `clientCertificateRequired` and `checkCertificateRevocation` allows better control over the SSL Context, 
and they are to `false` for test purpose.
Take a look at the [UseHttps](../reference/simplewserver.md#ssl-certificate) for more information.
:::


## Example for local test

The example bellow will use a pregerenarated certificate. Use only locally for testing, do not use in production !!

::: code-group

<<< @/snippets/ssl-certificate-example.cs#snippet{16-19,23,25 csharp:line-numbers} [program.cs]

:::


## Mutual Authentication

The server can also require the client to have a proper SSL Certificate.

Just add some check in the `SslContext`

::: code-group

<<< @/snippets/ssl-certificate-client-authentication.cs#snippet{5-12 csharp:line-numbers} [program.cs]

:::