using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using SimpleW.Observability;


namespace SimpleW.Service.LetsEncrypt {

    /// <summary>
    /// LetsEncrypt Options
    /// </summary>
    public sealed class LetsEncryptOptions {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<LetsEncryptOptions>();

        /// <summary>
        /// Domains to include in the certificate (CN + SAN)
        /// </summary>
        public string[] Domains { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Optional email for ACME account
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Storage folder for ACME account key + certificate files
        /// </summary>
        public string StoragePath { get; set; } = "./letsencrypt";

        /// <summary>
        /// Use Let's Encrypt staging environment (recommended for dev to avoid rate limits)
        /// </summary>
        public bool UseStaging { get; set; } = false;

        /// <summary>
        /// HTTP port used for HTTP-01 challenge (default 80)
        /// If SimpleWServer is running behind a reverse proxy
        /// (for example SimpleWServer listens on 192.168.1.2:8080),
        /// this value MUST be set to the internal listening port (e.g. 8080),
        /// not the public port exposed by the proxy.
        /// </summary>
        public int HttpPort { get; set; } = 80;

        /// <summary>
        /// HTTPS port to restore after issuance/renewal (default 443)
        /// If SimpleWServer is running behind a reverse proxy
        /// (for example SimpleWServer listens on 192.168.1.2:4443),
        /// this value MUST be set to the internal https listening port (e.g. 4443),
        /// not the public port exposed by the proxy.
        /// </summary>
        public int HttpsPort { get; set; } = 443;

        /// <summary>
        /// How long before expiration to renew (default 30 days)
        /// </summary>
        public TimeSpan RenewBefore { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// How often to check for renewal (default 12 hours)
        /// </summary>
        public TimeSpan CheckEvery { get; set; } = TimeSpan.FromHours(12);

        /// <summary>
        /// PFX password (optional). If null/empty, a random password is generated and persisted.
        /// </summary>
        public string? PfxPassword { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public LetsEncryptOptions() {
            OnEngineHttpsEnable = DefaultOnEngineHttpsEnable;
            OnEngineHttpsDisable = DefaultOnEngineHttpsDisable;
        }

        /// <summary>
        /// If true, module will automatically configure HTTPS with the new certificate.
        /// If false, module only writes the PFX to disk.
        /// </summary>
        public bool AutoConfigureHttps { get; set; } = true;

        /// <summary>
        /// Callback used to enable HTTPS on the selected engine after a certificate is loaded or renewed.
        /// The default implementation supports SimpleWEngine.
        /// </summary>
        public Action<SimpleWServer, X509Certificate2> OnEngineHttpsEnable { get; set; }

        /// <summary>
        /// Callback used to disable HTTPS on the selected engine before the HTTP-01 challenge.
        /// The default implementation supports SimpleWEngine.
        /// </summary>
        public Action<SimpleWServer> OnEngineHttpsDisable { get; set; }

        /// <summary>
        /// TLS protocols used by the default SimpleWEngine callback.
        /// </summary>
        public SslProtocols Protocols { get; set; } = SslProtocols.Tls12 | SslProtocols.Tls13;

        /// <summary>
        /// When loading PFX, which key storage flags to use.
        /// Default is EphemeralKeySet (good on Linux, avoids writing keys to disk store).
        /// </summary>
        public X509KeyStorageFlags KeyStorageFlags { get; set; } = OperatingSystem.IsWindows()
                                                                        ? (Environment.UserInteractive
                                                                            ? (X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet)
                                                                            : (X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet))
                                                                        : (X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

        /// <summary>
        /// Default OnEngineHttpsEnable for SimpleWEngine
        /// </summary>
        /// <param name="server"></param>
        /// <param name="certificate"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private void DefaultOnEngineHttpsEnable(SimpleWServer server, X509Certificate2 certificate) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(certificate);

            if (server.Engine is not SimpleWEngine engine) {
                throw new InvalidOperationException("OnEngineHttpsEnable must be configured for non-SimpleWEngine engines.");
            }

            engine.UseHttps(new SslContext(Protocols, certificate));
        }

        /// <summary>
        /// Default OnEngineHttpsDisable for SimpleWEngine
        /// </summary>
        /// <param name="server"></param>
        /// <exception cref="InvalidOperationException"></exception>
        private static void DefaultOnEngineHttpsDisable(SimpleWServer server) {
            ArgumentNullException.ThrowIfNull(server);

            if (server.Engine is not SimpleWEngine engine) {
                throw new InvalidOperationException("OnEngineHttpsDisable must be configured for non-SimpleWEngine engines.");
            }

            engine.DisableHttps();
        }

        /// <summary>
        /// Path used for ACME HTTP-01 challenge
        /// </summary>
        public string ChallengePath => "/.well-known/acme-challenge/:token";

        /// <summary>
        /// Enable Telemetry
        /// (the underlying SimpleWServer.Telemetry must be enabled)
        /// </summary>
        public bool EnableTelemetry { get; set; }

        /// <summary>
        /// Validate and normalize options
        /// </summary>
        public LetsEncryptOptions ValidateAndNormalize() {
            if (Domains == null || Domains.Length == 0 || Domains.Any(string.IsNullOrWhiteSpace)) {
                ArgumentException ex = new($"{nameof(Domains)} must contain at least one domain.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (HttpPort < 1 || HttpPort > 65535) {
                ArgumentException ex = new($"{nameof(HttpPort)} invalid port.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (HttpsPort < 1 || HttpsPort > 65535) {
                ArgumentException ex = new($"{nameof(HttpsPort)} invalid port.");
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (OnEngineHttpsEnable == null) {
                ArgumentNullException ex = new(nameof(OnEngineHttpsEnable));
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (OnEngineHttpsDisable == null) {
                ArgumentNullException ex = new(nameof(OnEngineHttpsDisable));
                _log.Fatal(ex.Message, ex);
                throw ex;
            }
            if (RenewBefore <= TimeSpan.Zero) {
                RenewBefore = TimeSpan.FromDays(30);
            }
            if (CheckEvery <= TimeSpan.Zero) {
                CheckEvery = TimeSpan.FromHours(12);
            }
            StoragePath = Path.GetFullPath(StoragePath);

            return this;
        }

    }

}
