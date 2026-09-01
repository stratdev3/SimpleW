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

    }

}
