namespace SimpleW.Service.Firewall {

    /// <summary>
    /// Runtime firewall attached to a SimpleW server.
    /// </summary>
    public interface IFirewall {

        /// <summary>
        /// Atomically updates the current global firewall configuration.
        /// Handler-specific metadata rules are not affected.
        /// </summary>
        /// <param name="update"></param>
        void Update(Action<FirewallOptions> update);

    }

}
