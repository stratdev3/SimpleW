using System.Runtime.CompilerServices;


namespace SimpleW.Helper.NpmTooling {

    /// <summary>
    /// Registers npm development supervision or production SPA hosting.
    /// </summary>
    public static class NpmToolingModuleExtension {

        private static readonly ConditionalWeakTable<SimpleWServer, NpmToolingModule> Instances = new();

        /// <summary>Installs the npm module once per server, before starting the listener.</summary>
        /// <param name="server">SimpleW server.</param>
        /// <param name="configure">Optional callback that configures the frontend before module installation.</param>
        /// <returns>The same server for fluent configuration.</returns>
        public static SimpleWServer UseNpmToolingModule(this SimpleWServer server, Action<NpmOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(server);
            NpmOptions options = new();
            configure?.Invoke(options);
            ArgumentNullException.ThrowIfNull(options.ServerLogger);
            ArgumentNullException.ThrowIfNull(options.ClientLogger);

            lock (Instances) {
                if (Instances.TryGetValue(server, out _)) {
                    throw new InvalidOperationException("The npm module is already installed for this server.");
                }
                NpmToolingModule module = new(options);
                server.UseModule(module);
                Instances.Add(server, module);
            }
            return server;
        }

    }

}
