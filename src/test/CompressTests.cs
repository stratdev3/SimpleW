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
        public async Task Response_200_Auto_ContentShouldNoBeCompressed_ClientNotSupport() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Auto_ContentShouldNoBeCompressed_ClientSupport() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return new { message = "Hello World !" };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Auto_ContentShouldBeCompressedClientNotSupport() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                string message = "Hello World";
                for (var i = 0; i < 10; i++) {
                    message += message;
                }
                return new { message };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Auto_ContentShouldBeCompressedClientSupport() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                string message = "Hello World";
                for (var i = 0; i < 10; i++) {
                    message += message;
                }
                return new { message };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Auto_ContentShouldBeCompressedToGzip() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                string message = "Hello World";
                for (var i = 0; i < 10; i++) {
                    message += message;
                }
                return new { message };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Auto_ContentShouldBeCompressedToGzipPriorityDeflate() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                string message = "Hello World";
                for (var i = 0; i < 10; i++) {
                    message += message;
                }
                return new { message };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, deflate, gzip, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Auto_ContentShouldBeCompressedToDeflate() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                string message = "Hello World";
                for (var i = 0; i < 10; i++) {
                    message += message;
                }
                return new { message };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("deflate");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_Auto_ContentShouldNotBeCompressed_ClientUnkownEncoding() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                string message = "Hello World";
                for (var i = 0; i < 10; i++) {
                    message += message;
                }
                return new { message };
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "unknown");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_ForceNoCompression() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                string message = "Hello World";
                for (var i = 0; i < 10; i++) {
                    message += message;
                }
                return session.Response.Json(new { message }).NoCompression();
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding).IsEmpty();

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_ForceGzip() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Json(new { message = "Hello World !" }).Compression(HttpResponse.ResponseCompressionMode.ForceGzip);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, deflate, gzip, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("gzip");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_ForceDeflate() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Json(new { message = "Hello World !" }).Compression(HttpResponse.ResponseCompressionMode.ForceDeflate);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("deflate");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Response_200_ForceBrotli() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", (HttpSession session) => {
                return session.Response.Json(new { message = "Hello World !" }).Compression(HttpResponse.ResponseCompressionMode.ForceBrotli);
            });
            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept-Encoding", "zstd, gzip, deflate, br");
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/");
            var content = await response.Content.ReadAsByteArrayAsync();

            // asserts
            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(response.Content.Headers.ContentEncoding.First()).IsEqualTo("br");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

    }

}
