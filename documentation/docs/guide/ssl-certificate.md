# SSL Certificate

The HTTPS protocol is supported and you can bring your own certificate in `PKCS#12` format.

With a little change the [basic static example](./static-files) can serve HTTPS.

::: code-group

<<< @/snippets/ssl-certificate.cs#snippet{csharp:line-numbers} [program.cs]

:::

There are 2 mains changes :
- a `context` creation pointing the certificat file which can be password protect.
- call to the `SimpleWSServer()` class to pass the context instead of `SimpleWServer()`.

