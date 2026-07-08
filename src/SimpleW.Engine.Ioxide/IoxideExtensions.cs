using ioxide;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// EngineExtension
    /// </summary>
    public static class IoxideExtensions {

        /// <summary>
        /// Use ioxide Engine
        /// </summary>
        /// <param name="server"></param>
        /// <param name="config"></param>
        /// <param name="onStart"></param>
        /// <returns></returns>
        public static SimpleWServer UseIoxideEngine(this SimpleWServer server, IoxideEngineOptions? config = null, Action<Reactor>? onStart = null) {
            ArgumentNullException.ThrowIfNull(server);
            return server.UseEngine(new IoxideEngine(config, onStart));
        }

        /// <summary>
        /// Use ioxide Engine
        /// </summary>
        /// <param name="server"></param>
        /// <param name="config"></param>
        /// <param name="onStart"></param>
        /// <returns></returns>
        public static SimpleWServer UseIoxideEngine(this SimpleWServer server, ServerConfig config, Action<Reactor>? onStart = null) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(config);
            return server.UseEngine(new IoxideEngine(config, onStart));
        }

        /// <summary>
        /// Use ioxide Engine
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <param name="onStart"></param>
        /// <returns></returns>
        public static SimpleWServer UseIoxideEngine(this SimpleWServer server, Func<IoxideEngineOptions, IoxideEngineOptions> configure, Action<Reactor>? onStart = null) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(configure);

            IoxideEngineOptions config = configure(new IoxideEngineOptions()) ?? throw new InvalidOperationException("Ioxide engine configuration must not return null.");
            return server.UseEngine(new IoxideEngine(config, onStart));
        }

    }

}