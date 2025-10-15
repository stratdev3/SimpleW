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

        //private static string unixSockerPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "UnixSockets", "server.sock");

        //private SocketsHttpHandler socketHttpHandler = new SocketsHttpHandler {
        //    ConnectCallback = async (context, cancellationToken) => {
        //        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        //        var endpoint = new UnixDomainSocketEndPoint(unixSockerPath);
        //        await socket.ConnectAsync(endpoint);
        //        return new NetworkStream(socket, ownsSocket: true);
        //    }
        //};

        //[Fact]
        //public async Task MapGet_HelloWorld() {

        //    if (!Socket.OSSupportsUnixDomainSockets) {
        //        return;
        //    }

        //    Directory.CreateDirectory(Path.Combine(unixSockerPath, ".."));

        //    // server
        //    var server = new SimpleWServer(new UnixDomainSocketEndPoint(unixSockerPath));
        //    server.MapGet("/", () => {
        //        return new { message = "Hello World !" };
        //    });
        //    server.Start();

        //    // client
        //    var client = new HttpClient(socketHttpHandler);
        //    var response = await client.GetAsync($"http://localhost/");
        //    var content = await response.Content.ReadAsStringAsync();

        //    // asserts
        //    Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        //    Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

        //    // dispose
        //    server.Stop();
        //    PortManager.ReleasePort(server.Port);
        //}

    }

}
