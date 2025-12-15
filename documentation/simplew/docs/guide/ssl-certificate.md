# SSL Certificate

The HTTPS protocol is supported and you can bring your own certificate in `PKCS#12` format.

With just a small change, the [basic static example](./static-files) can serve HTTPS.

::: code-group

<<< @/snippets/ssl-certificate.cs#snippet{14,17 csharp:line-numbers} [program.cs]

:::

There are 2 mains changes :
- L14 : a `context` creation pointing the certificat file which can be password protect.
- L17 : call to the `SimpleWSServer()` class to pass the context instead of `SimpleWServer()`.

