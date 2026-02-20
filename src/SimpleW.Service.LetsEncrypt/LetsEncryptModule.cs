using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Certes;                   // ACME (Certes) dotnet add package Certes
using Certes.Acme;              // ACME (Certes) dotnet add package Certes
using Certes.Acme.Resource;     // ACME (Certes) dotnet add package Certes
using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.LetsEncrypt {

    /// <summary>
    /// LetsEncryptModuleExtension
    /// </summary>
    public static class LetsEncryptModuleExtension {

        /// <summary>
        /// Use LetsEncrypt Module (HTTP-01, port 80)
        /// </summary>
        /// <example>
        /// server.UseLetsEncryptModule(o => {
        ///     o.Domains = [ "simplew.net", "www.simplew.net" ]
        ///     o.Email = "chris@simplew.net";
        ///     o.StoragePath = "/var/lib/simplew/letsencrypt";
        ///     o.UseStaging = true; // to avoid letsencrypt rate limit during tests
        /// });
        /// </example>
        public static SimpleWServer UseLetsEncryptModule(this SimpleWServer server, Action<LetsEncryptOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            LetsEncryptOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new LetsEncryptModule(options));
            return server;
        }

        /// <summary>
        /// LetsEncrypt Options
        /// </summary>
        public sealed class LetsEncryptOptions {

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
            /// If true, module will automatically call UseHttps(...) with the new certificate.
            /// If false, module only writes the PFX to disk.
            /// </summary>
            public bool AutoConfigureHttps { get; set; } = true;

            /// <summary>
            /// TLS protocols to enable in SslContext
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
            /// (Optional) build a custom SslContext from the certificate.
            /// If null, default SslContext is created with Protocols.
            /// </summary>
            public Func<X509Certificate2, SslContext>? SslContextFactory { get; set; }

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
                    throw new ArgumentException($"{nameof(LetsEncryptOptions)}.{nameof(Domains)} must contain at least one domain.");
                }
                StoragePath = Path.GetFullPath(StoragePath);

                if (HttpPort < 1 || HttpPort > 65535) {
                    throw new ArgumentException($"{nameof(LetsEncryptOptions)}.{nameof(HttpPort)} invalid port.");
                }
                if (HttpsPort < 1 || HttpsPort > 65535) {
                    throw new ArgumentException($"{nameof(LetsEncryptOptions)}.{nameof(HttpsPort)} invalid port.");
                }
                if (RenewBefore <= TimeSpan.Zero) {
                    RenewBefore = TimeSpan.FromDays(30);
                }
                if (CheckEvery <= TimeSpan.Zero) {
                    CheckEvery = TimeSpan.FromHours(12);
                }
                return this;
            }

        }

        /// <summary>
        /// LetsEncrypt Module (HTTP-01) for SimpleW
        /// </summary>
        private sealed class LetsEncryptModule : IHttpModule, IDisposable {

            /// <summary>
            /// Options
            /// </summary>
            private readonly LetsEncryptOptions _options;

            private readonly ConcurrentDictionary<string, string> _http01Store = new(StringComparer.Ordinal);
            private readonly SemaphoreSlim _singleFlight = new(1, 1);

            private SimpleWServer? _server;
            private CancellationTokenSource? _cts;
            private Task? _loop;

            // files
            private string AccountKeyPath => Path.Combine(_options.StoragePath, "acme-account.pem");
            private string PfxPath => Path.Combine(_options.StoragePath, "tls.pfx");
            private string PfxPasswordPath => Path.Combine(_options.StoragePath, "tls.pfx.pass");

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="options"></param>
            /// <exception cref="ArgumentNullException"></exception>
            public LetsEncryptModule(LetsEncryptOptions options) {
                _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
            }

            /// <summary>
            /// Install Module in server (called by SimpleW)
            /// </summary>
            /// <param name="server"></param>
            /// <exception cref="InvalidOperationException"></exception>
            public void Install(SimpleWServer server) {
                ArgumentNullException.ThrowIfNull(server);

                if (server.IsStarted) {
                    throw new InvalidOperationException("LetsEncryptModule must be installed before server start.");
                }

                _server = server;

                System.IO.Directory.CreateDirectory(_options.StoragePath);

                // register letsencrypt routes for HTTP-01
                server.MapGet(_options.ChallengePath, (HttpSession session, string token) => ChallengeHandlerAsync(session, token));
                server.Map("HEAD", _options.ChallengePath, (HttpSession session, string token) => ChallengeHeadHandlerAsync(session, token));

                // Startup orchestration
                server.OnStarted(async s => {
                    try {
                        StartBackgroundLoop();
                        // ensure on startup (fire once)
                        await EnsureCertificateAsync(force: false, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex) {
                        // don't crash server lifetime
                        Console.WriteLine($"[LetsEncrypt] Startup ensure failed: {ex}");
                    }
                });

                server.OnStopped(async s => {
                    try { await StopBackgroundLoopAsync().ConfigureAwait(false); }
                    catch { }
                });
            }

            /// <summary>
            /// Get Handler
            /// </summary>
            /// <param name="session"></param>
            /// <param name="token"></param>
            /// <returns></returns>
            private ValueTask ChallengeHandlerAsync(HttpSession session, string token) {
                if (_http01Store.TryGetValue(token, out string? keyAuth)) {
                    return session.Response.Status(200).Text(keyAuth).SendAsync();
                }
                return session.Response.NotFound("Not Found").SendAsync();
            }

            /// <summary>
            /// Head Handler
            /// </summary>
            /// <param name="session"></param>
            /// <param name="token"></param>
            /// <returns></returns>
            private ValueTask ChallengeHeadHandlerAsync(HttpSession session, string token) {
                // For HEAD we can just return 200 if token exists, else 404.
                if (_http01Store.ContainsKey(token)) {
                    return session.Response.Status(200).NoContentLength().SendAsync();
                }
                return session.Response.NotFound().NoContentLength().SendAsync();
            }

            #region background task

            /// <summary>
            /// Start Background Task
            /// </summary>
            private void StartBackgroundLoop() {
                if (_cts != null) {
                    return;
                }
                _cts = new CancellationTokenSource();
                _loop = Task.Run(() => RenewalLoopAsync(_cts.Token));
            }

            /// <summary>
            /// Stop Background Task
            /// </summary>
            /// <returns></returns>
            private async Task StopBackgroundLoopAsync() {
                if (_cts == null) {
                    return;
                }
                try { _cts.Cancel(); }
                catch { }

                Task? t = _loop;
                _loop = null;

                if (t != null) {
                    try { await t.ConfigureAwait(false); }
                    catch { }
                }

                _cts.Dispose();
                _cts = null;
            }

            /// <summary>
            /// Background Task
            /// </summary>
            /// <param name="ct"></param>
            /// <returns></returns>
            private async Task RenewalLoopAsync(CancellationToken ct) {
                while (!ct.IsCancellationRequested) {
                    try {
                        await Task.Delay(_options.CheckEvery, ct).ConfigureAwait(false);
                        await EnsureCertificateAsync(force: false, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) {
                        // normal
                    }
                    catch (Exception ex) {
                        Console.WriteLine($"[LetsEncrypt] Renewal loop error: {ex}");
                        // small backoff to avoid log spam if something is broken
                        try { await Task.Delay(TimeSpan.FromMinutes(2), ct).ConfigureAwait(false); }
                        catch { }
                    }
                }
            }

            #endregion background task

            #region issue renew certificate

            /// <summary>
            /// Load Certificate or Create/Renew
            /// </summary>
            /// <param name="force">true to force create</param>
            /// <param name="ct"></param>
            /// <returns></returns>
            /// <exception cref="InvalidOperationException"></exception>
            private async Task EnsureCertificateAsync(bool force, CancellationToken ct) {
                if (_server == null) {
                    throw new InvalidOperationException("Module not installed.");
                }

                // ensure gauges are registered when server telemetry is enabled
                EnsureTelemetry(_options.EnableTelemetry, _server);

                await _singleFlight.WaitAsync(ct).ConfigureAwait(false);
                try {
                    // 1) read local certificate
                    CertLoadStatus status = TryGetExistingCertificateInfo(out DateTime notAfterUtc, out Exception? loadError);

                    // 2) return on error
                    if (!force && status == CertLoadStatus.LoadError) {
                        Console.WriteLine($"[LetsEncrypt] ERROR: Existing certificate file exists but cannot be loaded: {PfxPath}");
                        Console.WriteLine($"[LetsEncrypt] ERROR details: {loadError}");
                        return;
                    }

                    // 3) certificate exists and not expired
                    if (!force && status == CertLoadStatus.Loaded) {
                        TimeSpan remaining = notAfterUtc - DateTime.UtcNow;

                        if (remaining > _options.RenewBefore) {
                            if (_options.AutoConfigureHttps) {
                                await SwitchToHttpsAsync(ct).ConfigureAwait(false);
                            }
                            Console.WriteLine($"[LetsEncrypt] Certificate OK. Expires in {remaining.TotalDays:F1} days.");
                            return;
                        }

                        Console.WriteLine($"[LetsEncrypt] Certificate expiring soon ({remaining.TotalDays:F1} days). Renewing...");
                    }

                    // 4) no certificate or force : create or renew
                    if (status == CertLoadStatus.NoCertificate) {
                        Console.WriteLine("[LetsEncrypt] No existing certificate found. Issuing a new certificate...");
                    }

                    Console.WriteLine("[LetsEncrypt] Switching to HTTP for HTTP-01 challenge...");
                    await SwitchToHttpAsync(ct).ConfigureAwait(false);

                    try {
                        await IssueOrRenewWithHttp01Async(ct).ConfigureAwait(false);
                    }
                    finally {
                        // switch back to https
                        if (_options.AutoConfigureHttps) {
                            await SwitchToHttpsAsync(ct).ConfigureAwait(false);
                        }
                    }
                }
                finally {
                    _singleFlight.Release();
                }
            }

            /// <summary>
            /// Switch to Https and load certificate
            /// </summary>
            /// <param name="ct"></param>
            /// <returns></returns>
            private async Task SwitchToHttpsAsync(CancellationToken ct) {
                if (_server == null) {
                    return;
                }
                if (!File.Exists(PfxPath)) {
                    return;
                }

                try {
                    string pass = LoadOrCreatePfxPassword();

#if NET9_0_OR_GREATER
                    X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(PfxPath, pass, _options.KeyStorageFlags);
#else
                    X509Certificate2 cert = new(PfxPath, pass, _options.KeyStorageFlags);
#endif

                    DateTime notAfterUtc = cert.NotAfter.ToUniversalTime();
                    Volatile.Write(ref _certNotAfterUtcTicks, notAfterUtc.Ticks);
                    Volatile.Write(ref _certLoaded, 1);

                    Console.WriteLine($"[LetsEncrypt] Subject: {cert.Subject}");
                    Console.WriteLine($"[LetsEncrypt] NotAfter: {cert.NotAfter:O}");
                    Console.WriteLine($"[LetsEncrypt] HasPrivateKey: {cert.HasPrivateKey}");

                    SslContext ssl = _options.SslContextFactory?.Invoke(cert) ?? new SslContext(_options.Protocols, cert);

                    // Switch to HTTPS only if not already on the desired port/ssl.
                    await _server.ReloadListenerAsync(s => {
                        s.UsePort(_options.HttpsPort);
                        s.UseHttps(ssl);
                    }, ct).ConfigureAwait(false);

                    Console.WriteLine("[LetsEncrypt] HTTPS listener configured.");
                }
                catch (Exception ex) {
                    Console.WriteLine($"[LetsEncrypt] Failed to configure HTTPS: {ex}");
                }
            }

            /// <summary>
            /// Switch to Http
            /// </summary>
            /// <param name="ct"></param>
            /// <returns></returns>
            private async Task SwitchToHttpAsync(CancellationToken ct) {
                if (_server == null) {
                    return;
                }

                await _server.ReloadListenerAsync(s => {
                    s.DisableHttps();
                    s.UsePort(_options.HttpPort);
                }, ct).ConfigureAwait(false);

                Console.WriteLine("[LetsEncrypt] Listener switched to HTTP.");
            }

            private async Task IssueOrRenewWithHttp01Async(CancellationToken ct) {
                //
                // ACME account key (persisted)
                //
                IKey accountKey;
                if (File.Exists(AccountKeyPath)) {
                    string pem = await File.ReadAllTextAsync(AccountKeyPath, ct).ConfigureAwait(false);
                    accountKey = KeyFactory.FromPem(pem);
                }
                else {
                    accountKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
                    string pem = accountKey.ToPem();
                    await File.WriteAllTextAsync(AccountKeyPath, pem, ct).ConfigureAwait(false);
                }

                Uri letsEncryptUrlAPI = _options.UseStaging ? WellKnownServers.LetsEncryptStagingV2 : WellKnownServers.LetsEncryptV2;
                AcmeContext acme = new(letsEncryptUrlAPI, accountKey);

                // set email if provided
                if (!string.IsNullOrWhiteSpace(_options.Email)) {
                    try {
                        await acme.NewAccount(_options.Email!, termsOfServiceAgreed: true).ConfigureAwait(false);
                    }
                    catch {
                        // account might already exist for that key; ignore
                    }
                }
                else {
                    // still need to agree ToS on first account creation, but with an existing key, it’s already done.
                    try { await acme.NewAccount("mailto:unknown@example.invalid", termsOfServiceAgreed: true).ConfigureAwait(false); }
                    catch { }
                }

                // New order
                IOrderContext order = await acme.NewOrder(_options.Domains).ConfigureAwait(false);

                //
                // Authorizations + HTTP-01 challenges
                //
                IAuthorizationContext[] authz = (await order.Authorizations().ConfigureAwait(false)).ToArray();
                List<string> activatedTokens = new(authz.Length);

                try {
                    foreach (IAuthorizationContext a in authz) {
                        IChallengeContext httpChallenge = await a.Http().ConfigureAwait(false);

                        // Certes exposes token + key authorization
                        // (Property name is KeyAuthz in Certes)
                        string token = httpChallenge.Token;
                        string keyAuth = httpChallenge.KeyAuthz;

                        _http01Store[token] = keyAuth;
                        activatedTokens.Add(token);

                        Console.WriteLine($"[LetsEncrypt] HTTP-01 armed for token: {token}");

                        // ask CA to validate
                        await httpChallenge.Validate().ConfigureAwait(false);
                    }

                    // Wait for validation
                    // Poll order status until ready/valid/invalid
                    while (true) {
                        ct.ThrowIfCancellationRequested();

                        Order o = await order.Resource().ConfigureAwait(false);
                        if (o.Status == OrderStatus.Valid) {
                            break;
                        }
                        if (o.Status == OrderStatus.Invalid) {
                            throw new InvalidOperationException("ACME order became invalid (check port 80 reachability and domain DNS).");
                        }

                        // pending / ready / processing
                        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);

                        // If ready, finalize (CSR)
                        if (o.Status == OrderStatus.Ready) {
                            break;
                        }
                    }

                    // Finalize + download certificate
                    // Create a fresh private key for the leaf cert
                    IKey certKey = KeyFactory.NewKey(KeyAlgorithm.RS256);

                    CsrInfo csr = new() {
                        CommonName = _options.Domains[0],
                    };

                    // Generate + download chain
                    // In Certes, Generate returns a CertificateChain
                    CertificateChain chain = await order.Generate(csr, certKey).ConfigureAwait(false);

                    // Export PFX
                    string pass = LoadOrCreatePfxPassword();
                    byte[] pfx = chain.ToPfx(certKey).Build(_options.Domains[0], pass);

                    // Atomic write
                    string tmp = PfxPath + ".tmp";
                    await File.WriteAllBytesAsync(tmp, pfx, ct).ConfigureAwait(false);
                    File.Move(tmp, PfxPath, overwrite: true);

                    Console.WriteLine("[LetsEncrypt] New certificate written to tls.pfx");

#if NET9_0_OR_GREATER
                    X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(PfxPath, pass, _options.KeyStorageFlags);
#else
                    X509Certificate2 cert = new(PfxPath, pass, _options.KeyStorageFlags);
#endif

                    DateTime notAfterUtc = cert.NotAfter.ToUniversalTime();
                    Volatile.Write(ref _certNotAfterUtcTicks, notAfterUtc.Ticks);
                    Volatile.Write(ref _certLoaded, 1);

                    // Switch back to HTTPS with fresh cert
                    if (_options.AutoConfigureHttps && _server != null) {
                        SslContext ssl = _options.SslContextFactory?.Invoke(cert) ?? new SslContext(_options.Protocols, cert);

                        await _server.ReloadListenerAsync(s => {
                            s.UsePort(_options.HttpsPort);
                            s.UseHttps(ssl);
                        }, ct).ConfigureAwait(false);

                        Console.WriteLine("[LetsEncrypt] Listener switched back to HTTPS.");
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine($"[LetsEncrypt] Failed to load fresh certificate after issuance: {ex}");
                    Volatile.Write(ref _certNotAfterUtcTicks, 0);
                    Volatile.Write(ref _certLoaded, 0);
                }
                finally {
                    // Clean challenge tokens from memory store
                    foreach (string t in activatedTokens) {
                        _http01Store.TryRemove(t, out _);
                    }
                }
            }

            #endregion issue renew certificate

            #region load certificate

            /// <summary>
            /// Try Load Certificate
            /// </summary>
            /// <param name="notAfterUtc"></param>
            /// <param name="error"></param>
            /// <returns></returns>
            private CertLoadStatus TryGetExistingCertificateInfo(out DateTime notAfterUtc, out Exception? error) {
                notAfterUtc = default;
                error = null;

                if (!File.Exists(PfxPath)) {
                    Volatile.Write(ref _certNotAfterUtcTicks, 0);
                    Volatile.Write(ref _certLoaded, 0);
                    return CertLoadStatus.NoCertificate;
                }

                try {
                    string pass = LoadOrCreatePfxPassword();

#if NET9_0_OR_GREATER
                    using X509Certificate2 cert = X509CertificateLoader.LoadPkcs12FromFile(PfxPath, pass, _options.KeyStorageFlags);
#else
                    using X509Certificate2 cert = new X509Certificate2(PfxPath, pass, _options.KeyStorageFlags);
#endif
                    notAfterUtc = cert.NotAfter.ToUniversalTime();
                    Volatile.Write(ref _certNotAfterUtcTicks, notAfterUtc.Ticks);
                    Volatile.Write(ref _certLoaded, 1);
                    return CertLoadStatus.Loaded;
                }
                catch (Exception ex) {
                    error = ex;
                    Volatile.Write(ref _certNotAfterUtcTicks, 0);
                    Volatile.Write(ref _certLoaded, 0);
                    return CertLoadStatus.LoadError;
                }
            }

            /// <summary>
            /// Certificate Password
            /// </summary>
            /// <returns></returns>
            /// <exception cref="InvalidOperationException"></exception>
            private string LoadOrCreatePfxPassword() {
                if (!string.IsNullOrWhiteSpace(_options.PfxPassword)) {
                    return _options.PfxPassword!;
                }

                if (File.Exists(PfxPasswordPath)) {
                    return File.ReadAllText(PfxPasswordPath).Trim();
                }

                if (File.Exists(PfxPath)) {
                    throw new InvalidOperationException($"PFX exists but password file is missing: {PfxPasswordPath}");
                }

                string pass = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                File.WriteAllText(PfxPasswordPath, pass);
                return pass;
            }

            /// <summary>
            /// Certificate Loading Status
            /// </summary>
            private enum CertLoadStatus {
                NoCertificate,
                Loaded,
                LoadError
            }

            #endregion load certificate

            #region telemetry

            /// <summary>
            /// Telemetry (lazy)
            /// </summary>
            private LetsEncryptTelemetry? _telemetry;

            /// <summary>
            /// Telemetry lock
            /// </summary>
            private readonly object _telemetryLock = new();

            /// <summary>
            /// Last known certificate NotAfter (UTC ticks). 0 = unknown/no cert.
            /// </summary>
            private long _certNotAfterUtcTicks;

            /// <summary>
            /// 1 if a certificate file exists and was loaded successfully, else 0.
            /// </summary>
            private int _certLoaded;

            /// <summary>
            /// Ensure LetsEncrypt telemetry depending on the underlying server.Telemetry
            /// </summary>
            private LetsEncryptTelemetry? EnsureTelemetry(bool enable, SimpleWServer server) {
                if (!enable) {
                    return null;
                }
                Telemetry? telemetry = server.Telemetry;
                if (telemetry == null || !server.IsTelemetryEnabled) {
                    return null;
                }
                LetsEncryptTelemetry? t = _telemetry;
                if (t != null) {
                    return t;
                }
                lock (_telemetryLock) {
                    _telemetry ??= new LetsEncryptTelemetry(telemetry.Meter, this);
                    return _telemetry;
                }
            }

            /// <summary>
            /// LetsEncryptTelemetry
            /// </summary>
            private sealed class LetsEncryptTelemetry {

                /// <summary>
                /// Remaining validity of the currently loaded certificate (days).
                /// -1 when unknown/no certificate.
                /// </summary>
                public readonly ObservableGauge<double> CertificateRemainingDays;

                /// <summary>
                /// Whether a certificate is currently loaded (0/1).
                /// </summary>
                public readonly ObservableGauge<int> CertificateLoaded;

                /// <summary>
                /// Constructor
                /// </summary>
                /// <param name="meter"></param>
                /// <param name="module"></param>
                public LetsEncryptTelemetry(Meter meter, LetsEncryptModule module) {
                    CertificateRemainingDays = meter.CreateObservableGauge<double>(
                                                    "simplew.letsencrypt.certificate.remaining_days",
                                                    () => {
                                                        long ticks = Volatile.Read(ref module._certNotAfterUtcTicks);
                                                        if (ticks <= 0) {
                                                            return -1d;
                                                        }
                                                        DateTime notAfterUtc = new DateTime(ticks, DateTimeKind.Utc);
                                                        double days = (notAfterUtc - DateTime.UtcNow).TotalDays;
                                                        return days < 0 ? 0d : days;
                                                    },
                                                    unit: "d"
                                                );

                    CertificateLoaded = meter.CreateObservableGauge<int>(
                                            "simplew.letsencrypt.certificate.loaded",
                                            () => Volatile.Read(ref module._certLoaded),
                                            unit: "bool"
                                        );
                }
            }

            #endregion telemetry

            /// <summary>
            /// Dispose
            /// </summary>
            public void Dispose() {
                try { _cts?.Cancel(); }
                catch { }
                try { _cts?.Dispose(); }
                catch { }
                _cts = null;
                _loop = null;
            }

        }

    }

}
