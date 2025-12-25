// create a context with certificate, support for password protection
var context = new SslContext(
    SslProtocols.Tls12 | SslProtocols.Tls13,
    cert,
    (sender, certificate, chain, sslPolicyErrors) => {
        if (sslPolicyErrors != SslPolicyErrors.None) {
            Console.WriteLine($"[MTLS] failed to validate client certificate : {sslPolicyErrors}");
            return false; // rejette la connexion si erreur
        }
        Console.WriteLine($"[MTLS] client certificate accepted : {certificate.Subject}");
        return true;  // accepte la connexion
    }
);
