using System.Net.Sockets;
using System.Runtime.InteropServices;


namespace SimpleW {

    /// <summary>
    /// Socket Extensions
    /// </summary>
    internal static class SocketExtensions {

        private const int SOL_SOCKET = 1;
        private const int SO_REUSEPORT = 15;

        [DllImport("libc", SetLastError = true)]
        private static extern int setsockopt(int sockfd, int level, int optname, ref int optval, uint optlen);

        /// <summary>
        /// ReusePort on Linux
        /// </summary>
        /// <param name="socket"></param>
        /// <exception cref="SocketException"></exception>
        public static void EnableReusePort(this Socket socket) {

            // only on Linux, it's a non sens on Windows
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
                Console.WriteLine($"[SimpleW] Warning: SO_REUSEPORT failed with errno {errno}");
                throw new SocketException(errno);
            }
            else {
                Console.WriteLine("[SimpleW] SO_REUSEPORT enabled");
            }
        }
    }

}
