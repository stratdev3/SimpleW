namespace SimpleW.Service.Latency {

    /// <summary>
    /// LatencyModuleExtension
    /// </summary>
    public static class LatencyModuleExtension {

        /// <summary>
        /// Use Latency Module
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseLatencyModule(this SimpleWServer server, Action<LatencyOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            LatencyOptions options = new();
            configure?.Invoke(options);

            server.UseModule(new LatencyModule(options));
            return server;
        }
    }

}
