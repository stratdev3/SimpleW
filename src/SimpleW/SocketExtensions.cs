using System.Net.Sockets;
using System.Runtime.InteropServices;
using SimpleW.Observability;


namespace SimpleW {

    /// <summary>
    /// Socket Extensions
    /// </summary>
    internal static class SocketExtensions {

        private static readonly ILogger _log = new Logger("SocketExtensions");

        private const int SOL_SOCKET = 1;
        private const int SO_REUSEPORT = 15;

        /// <summary>
        /// Linux PInvoke
        /// </summary>
        /// <param name="sockfd"></param>
        /// <param name="level"></param>
        /// <param name="optname"></param>
        /// <param name="optval"></param>
        /// <param name="optlen"></param>
        /// <returns></returns>
        [DllImport("libc", SetLastError = true)]
        private static extern int setsockopt(int sockfd, int level, int optname, ref int optval, uint optlen);

        /// <summary>
        /// ReusePort on Linux
        /// </summary>
        /// <param name="socket"></param>
        /// <exception cref="SocketException"></exception>
        public static void EnableReusePort(this Socket socket) {

            // only on Linux, it's a nonsense on Windows
            if (!OperatingSystem.IsLinux()) {
                return;
            }

            // only works for TCP sockets
            if (socket.SocketType != SocketType.Stream) {
                return;
            }

            // be careful : called before the Bind()
            int fd = (int)socket.Handle; 
            int opt = 1;
            int result = setsockopt(fd, SOL_SOCKET, SO_REUSEPORT, ref opt, sizeof(int));

            if (result != 0) {
                int errno = Marshal.GetLastWin32Error();
                SocketException ex = new (errno);
                _log.Warn("SO_REUSEPORT failed", ex);
                throw ex;
            }
            else {
                _log.Info("SO_REUSEPORT enabled");
            }
        }
    }

}
