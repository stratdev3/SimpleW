namespace SimpleW.Service.Chaos {

    /// <summary>
    /// ChaosModuleExtension
    /// </summary>
    public static class ChaosModuleExtension {

        /// <summary>
        /// Use Chaos Module (inject HTTP errors / connection failures)
        /// </summary>
        /// <example>
        /// server.UseChaosModule(o => {
        ///     o.Enabled = true;
        ///     o.Prefix = "/api";
        ///     o.Probability = 0.10; // 10% of requests are sabotaged
        ///     o.CloseConnectionProbability = 0.20; // 20% of sabotaged requests: connection drop
        ///     o.AbortWithRst = true; // RST instead of graceful close
        ///     o.StatusWeights = new Dictionary int, double {
        ///         [403] = 1,
        ///         [404] = 2,
        ///         [500] = 4
        ///     };
        ///     o.MinDelayMs = 0;
        ///     o.MaxDelayMs = 250;
        /// });
        /// </example>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseChaosModule(this SimpleWServer server, Action<ChaosOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            ChaosOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new ChaosModule(options));
            return server;
        }

    }

}
