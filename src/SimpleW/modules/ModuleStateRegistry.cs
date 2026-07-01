using System.Runtime.CompilerServices;


namespace SimpleW.Modules {

    /// <summary>
    /// Per-server registry for module state.
    /// </summary>
    /// <typeparam name="TConfiguration"></typeparam>
    public sealed class ModuleStateRegistry<TConfiguration> where TConfiguration : class {

        /// <summary>
        /// States
        /// </summary>
        private readonly ConditionalWeakTable<SimpleWServer, ModuleState<TConfiguration>> _states = new();

        /// <summary>
        /// Returns the state associated with the given server.
        /// </summary>
        /// <param name="server"></param>
        /// <returns></returns>
        public ModuleState<TConfiguration> Get(SimpleWServer server) {
            ArgumentNullException.ThrowIfNull(server);

            return _states.GetValue(server, static _ => new ModuleState<TConfiguration>());
        }

    }

    /// <summary>
    /// Per-server mutable state for a module configuration.
    /// </summary>
    /// <typeparam name="TConfiguration"></typeparam>
    public sealed class ModuleState<TConfiguration> where TConfiguration : class {

        /// <summary>
        /// lock
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Configuration
        /// </summary>
        private TConfiguration? _configuration;

        /// <summary>
        /// Flag
        /// </summary>
        private bool _isInstalled;

        /// <summary>
        /// Current module configuration snapshot.
        /// </summary>
        public TConfiguration? Snapshot => Volatile.Read(ref _configuration);

        /// <summary>
        /// Replace the current module configuration.
        /// </summary>
        /// <param name="configuration"></param>
        public void SetConfiguration(TConfiguration configuration) {
            ArgumentNullException.ThrowIfNull(configuration);

            lock (_syncRoot) {
                _configuration = configuration;
            }
        }

        /// <summary>
        /// Replace the current module configuration after validating an existing snapshot.
        /// </summary>
        /// <param name="configuration"></param>
        /// <param name="validateReplacement"></param>
        public void SetConfiguration(TConfiguration configuration, Action<TConfiguration, TConfiguration> validateReplacement) {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(validateReplacement);

            lock (_syncRoot) {
                if (_configuration != null) {
                    validateReplacement(_configuration, configuration);
                }

                _configuration = configuration;
            }
        }

        /// <summary>
        /// Mark the module as installed once for this server.
        /// </summary>
        /// <returns></returns>
        public bool TryMarkInstalled() {
            lock (_syncRoot) {
                if (_isInstalled) {
                    return false;
                }

                _isInstalled = true;
                return true;
            }
        }

    }

}