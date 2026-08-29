using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for UnixSockets
    /// </summary>
    public class UnixSockets {

        [Fact]
        public async Task MapGet_Should_Work_Over_UnixDomainSocket() {

            if (!Socket.OSSupportsUnixDomainSockets) {
                return;
            }

            string unixSocketPath = CreateUnixSocketPath();
            UnixDomainSocketEndPoint endpoint = new(unixSocketPath);
            var server = new SimpleWServer(endpoint);
            server.MapGet("/", () => new { message = "Hello World !" });

            try {
                Check.That(server.IsUnixDomainSocket).IsTrue();
                Check.That(server.EndPoint).IsInstanceOf<UnixDomainSocketEndPoint>();

                await server.StartAsync();

                using SocketsHttpHandler handler = CreateUnixSocketHandler(endpoint);
                using HttpClient client = new(handler, disposeHandler: false);
                using HttpResponseMessage response = await client.GetAsync("http://localhost/");
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            }
            finally {
                try {
                    await server.StopAsync();
                }
                finally {
                    DeleteUnixSocketFile(unixSocketPath);
                }
            }
        }

        [Fact]
        public async Task Server_Should_Restart_On_Same_UnixDomainSocket() {

            if (!Socket.OSSupportsUnixDomainSockets) {
                return;
            }

            string unixSocketPath = CreateUnixSocketPath();
            UnixDomainSocketEndPoint endpoint = new(unixSocketPath);
            var server = new SimpleWServer(endpoint);
            server.MapGet("/", () => new { message = "Hello World !" });

            try {
                await server.StartAsync();
                Check.That(File.Exists(unixSocketPath)).IsTrue();
                await AssertHelloWorldAsync(endpoint);

                await server.StopAsync();
                Check.That(File.Exists(unixSocketPath)).IsFalse();

                await server.StartAsync();
                await AssertHelloWorldAsync(endpoint);
            }
            finally {
                try {
                    await server.StopAsync();
                }
                finally {
                    DeleteUnixSocketFile(unixSocketPath);
                }
            }
        }

        private static string CreateUnixSocketPath() => Path.Combine(Path.GetTempPath(), $"sw-{Guid.NewGuid():N}.sock");

        private static SocketsHttpHandler CreateUnixSocketHandler(UnixDomainSocketEndPoint endpoint) {
            return new SocketsHttpHandler {
                ConnectCallback = async (_, cancellationToken) => {
                    Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try {
                        await socket.ConnectAsync(endpoint, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch {
                        socket.Dispose();
                        throw;
                    }
                }
            };
        }

        private static async Task AssertHelloWorldAsync(UnixDomainSocketEndPoint endpoint) {
            using SocketsHttpHandler handler = CreateUnixSocketHandler(endpoint);
            using HttpClient client = new(handler, disposeHandler: false);
            using HttpResponseMessage response = await client.GetAsync("http://localhost/");
            string content = await response.Content.ReadAsStringAsync();

            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
        }

        private static void DeleteUnixSocketFile(string unixSocketPath) {
            if (File.Exists(unixSocketPath)) {
                File.Delete(unixSocketPath);
            }
        }

    }

}
