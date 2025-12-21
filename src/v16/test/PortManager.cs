using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;


namespace test {

    /// <summary>
    /// PortManager class
    /// </summary>
    public static class PortManager {

        /// <summary>
        /// Current Used Ports
        /// </summary>
        private static readonly ConcurrentDictionary<int, bool> _usedPorts = new();

        /// <summary>
        /// Start range
        /// </summary>
        private const int StartPort = 2015;

        /// <summary>
        /// End range
        /// </summary>
        private const int EndPort = 20150;

        /// <summary>
        /// Max Try getting available port
        /// </summary>
        private const int MaxTry = 50;

        /// <summary>
        /// Get Available Port
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static int GetFreePort() {
            for (int i = 0; i < MaxTry; i++) {
                int port = GetSystemFreePort();
                if (_usedPorts.TryAdd(port, true)) {
                    return port;
                }
            }
            throw new Exception("no avaiable port.");
        }

        private static int GetSystemFreePort() {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }

        public static void ReleasePort(int port) {
            _usedPorts.TryRemove(port, out _);
        }

        public static int[] GetAllocatedPorts() {
            return _usedPorts.Keys.ToArray();
        }

    }

}
