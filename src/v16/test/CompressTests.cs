using System.Net;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for compress
    /// </summary>
    public class CompressTests {

        [Fact]
        public async Task Response_200() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_gzip() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");
            Check.That(content).IsEqualTo(NetCoreServer.HttpResponse.Compress(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message = "Hello World !" })), "gzip"));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_deflate() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, deflate, gzip, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("deflate");
            Check.That(content).IsEqualTo(NetCoreServer.HttpResponse.Compress(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message = "Hello World !" })), "deflate"));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_br() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, br, deflate, gzip");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("br");
            Check.That(content).IsEqualTo(NetCoreServer.HttpResponse.Compress(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message = "Hello World !" })), "br"));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_unknown() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return new { message = "Hello World !" };
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "unknown");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_force_disable_default() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return session.Response.MakeResponse(new { message = "Hello World !" });
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_force_disable_explicit() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return session.Response.MakeResponse(new { message = "Hello World !" }, compress: null);
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_forced() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (ISimpleWSession session) => {
                return session.Response.MakeResponse(new { message = "Hello World !" }, compress: new string[] { "gzip" });
            });
            server.Start();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");
            Check.That(content).IsEqualTo(NetCoreServer.HttpResponse.Compress(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { message = "Hello World !" })), "gzip"));

            // dispose
            server.Stop();
            PortManager.ReleasePort(server.Port);
        }

    }

}
