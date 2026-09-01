using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Firewall {

    /// <summary>
    /// Firewall module extensions for SimpleW.
    /// </summary>
    public static class FirewallModuleExtension {

        /// <summary>
        /// Logger
        /// </summary>
        private static readonly ILogger _log = new Logger<FirewallModule>();

        /// <summary>
        /// Associates one background service instance with each SimpleW server without extending server lifetime.
        /// </summary>
        private static readonly ModuleInstanceRegistry<FirewallModule> _instances = new();

        /// <summary>
        /// Object lock
        /// </summary>
        private static readonly object _registrationLock = new();

        /// <summary>
        /// Enables the firewall module on the current server.
        /// The module can only be installed once; use GetFirewall().Update(...) for runtime changes.
        /// Global rules are configured here; handler-specific rules are declared with firewall metadata attributes.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public static SimpleWServer UseFirewallModule(this SimpleWServer server, Action<FirewallOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);

            ThrowIfInstalled(server);

            FirewallOptions options = new();
            configure?.Invoke(options);
            options.ValidateAndNormalize();

            FirewallOptionsSnapshot configuration = FirewallOptionsSnapshot.FromOptions(options);

            lock (_registrationLock) {
                ThrowIfInstalled(server);
                server.UseModule(new FirewallModule(configuration));
            }

            _log.Info("installed");
            return server;
        }

        private static void ThrowIfInstalled(SimpleWServer server) {
            if (_instances.TryGet(server, out _)) {
                throw new InvalidOperationException("Firewall module is already installed for this server. Use GetFirewall().Update(...) to change its configuration.");
            }
        }

        #region service resolution

        /// <summary>
        /// Gets the firewall attached to a server.
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public static IFirewall GetFirewall(this SimpleWServer server) {
            ArgumentNullException.ThrowIfNull(server);

            if (_instances.TryGet(server, out FirewallModule? instance) && instance != null) {
                return instance;
            }

            throw new InvalidOperationException("Firewall module is not installed. Call UseFirewallModule(...) before using the firewall.");
        }

        /// <summary>
        /// Gets the firewall attached to the current server.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public static IFirewall GetFirewall(this HttpSession session) {
            ArgumentNullException.ThrowIfNull(session);
            return session.Server.GetFirewall();
        }

        /// <summary>
        /// Gets the firewall attached to the current server.
        /// </summary>
        /// <param name="controller"></param>
        /// <returns></returns>
        public static IFirewall GetFirewall(this Controller controller) {
            ArgumentNullException.ThrowIfNull(controller);
            return controller.Session.GetFirewall();
        }

        #endregion service resolution

        #region internal registration

        /// <summary>
        /// Registers the firewall attached to the current server.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="instance"></param>
        /// <exception cref="InvalidOperationException"></exception>
        internal static void Register(SimpleWServer server, FirewallModule instance) {
            if (!_instances.TryAdd(server, instance)) {
                throw new InvalidOperationException("Firewall module is already installed for this server.");
            }
        }

        #endregion internal registration

    }

}
