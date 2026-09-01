using ioxide;


namespace SimpleW.Engine.Ioxide {

    /// <summary>
    /// SimpleW extensions for selecting the ioxide network engine.
    /// </summary>
    public static class IoxideExtensions {

        /// <summary>
        /// Replaces the current SimpleW engine with an ioxide engine.
        /// </summary>
        /// <param name="server">Server to configure.</param>
        /// <param name="options">Ioxide integration options.</param>
        /// <param name="onReactorStart">Optional callback executed on every reactor thread before its loop starts.</param>
        /// <returns>The configured server.</returns>
        public static SimpleWServer UseIoxideEngine(this SimpleWServer server, IoxideEngineOptions? options = null, Action<Reactor>? onReactorStart = null) {
            ArgumentNullException.ThrowIfNull(server);
            return server.UseEngine(new IoxideEngine(options, onReactorStart));
        }

        /// <summary>
        /// Replaces the current SimpleW engine with an ioxide engine using a native server configuration.
        /// </summary>
        /// <param name="server">Server to configure.</param>
        /// <param name="config">Native ioxide configuration.</param>
        /// <param name="onReactorStart">Optional callback executed on every reactor thread before its loop starts.</param>
        /// <returns>The configured server.</returns>
        public static SimpleWServer UseIoxideEngine(this SimpleWServer server, ServerConfig config, Action<Reactor>? onReactorStart = null) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(config);
            return server.UseEngine(new IoxideEngine(config, onReactorStart));
        }

        /// <summary>
        /// Replaces the current SimpleW engine with an ioxide engine configured in place.
        /// </summary>
        /// <param name="server">Server to configure.</param>
        /// <param name="configure">Options callback.</param>
        /// <param name="onReactorStart">Optional callback executed on every reactor thread before its loop starts.</param>
        /// <returns>The configured server.</returns>
        public static SimpleWServer UseIoxideEngine(this SimpleWServer server, Action<IoxideEngineOptions> configure, Action<Reactor>? onReactorStart = null) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(configure);

            IoxideEngineOptions options = new();
            configure(options);
            return server.UseEngine(new IoxideEngine(options, onReactorStart));
        }

        /// <summary>
        /// Replaces the current SimpleW engine using a functional options callback.
        /// This overload preserves the configuration shape used by earlier prereleases.
        /// </summary>
        /// <param name="server">Server to configure.</param>
        /// <param name="configure">Callback that returns the configured options.</param>
        /// <param name="onReactorStart">Optional callback executed on every reactor thread before its loop starts.</param>
        /// <returns>The configured server.</returns>
        public static SimpleWServer UseIoxideEngine(this SimpleWServer server, Func<IoxideEngineOptions, IoxideEngineOptions> configure, Action<Reactor>? onReactorStart = null) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(configure);

            IoxideEngineOptions options = configure(new IoxideEngineOptions()) ?? throw new InvalidOperationException("The ioxide configuration callback returned null.");
            return server.UseEngine(new IoxideEngine(options, onReactorStart));
        }

    }

}
