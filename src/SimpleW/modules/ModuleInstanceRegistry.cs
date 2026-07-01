using System.Runtime.CompilerServices;


namespace SimpleW.Modules {

    /// <summary>
    /// Per-server registry for module runtime instances.
    /// </summary>
    /// <typeparam name="TInstance"></typeparam>
    public sealed class ModuleInstanceRegistry<TInstance> where TInstance : class {

        /// <summary>
        /// Instances
        /// </summary>
        private readonly ConditionalWeakTable<SimpleWServer, TInstance> _instances = new();

        /// <summary>
        /// Adds an instance for the given server if none is already registered.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="instance"></param>
        /// <returns></returns>
        public bool TryAdd(SimpleWServer server, TInstance instance) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(instance);

            try {
                _instances.Add(server, instance);
                return true;
            }
            catch (ArgumentException) {
                return false;
            }
        }

        /// <summary>
        /// Attempts to get the instance associated with the given server.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="instance"></param>
        /// <returns></returns>
        public bool TryGet(SimpleWServer server, out TInstance? instance) {
            ArgumentNullException.ThrowIfNull(server);

            return _instances.TryGetValue(server, out instance);
        }

    }

}