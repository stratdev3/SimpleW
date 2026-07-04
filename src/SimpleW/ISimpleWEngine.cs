using System.Net;
using System.Net.Sockets;


namespace SimpleW {

    /// <summary>
    /// Network engine used by <see cref="SimpleWServer"/> to accept connections.
    /// </summary>
    public interface ISimpleWEngine {

        /// <summary>
        /// Engine display name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Start the engine and begin accepting connections.
        /// Return the effective bound endpoint when it differs from the configured one.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="options"></param>
        /// <param name="connectionHandler"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task<EndPoint?> StartAsync(
            SimpleWServer server,
            SimpleWSServerOptions options,
            Func<Socket, Task> connectionHandler,
            CancellationToken cancellationToken = default
        );

        /// <summary>
        /// Stop the engine and release its listener resources.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task StopAsync(SimpleWServer server, CancellationToken cancellationToken = default);

    }

}
