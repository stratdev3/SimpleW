using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using SimpleW;
using Xunit;


namespace test {

    /// <summary>
    /// Tests response metadata validation against HTTP response splitting.
    /// </summary>
    public class ResponseSplittingTests {

        [Theory]
        [InlineData("OK\rX-Injected: yes")]
        [InlineData("OK\nX-Injected: yes")]
        [InlineData("OK\r\nX-Injected: yes")]
        [InlineData("OK\0X-Injected: yes")]
        [InlineData("OK\u001fX-Injected: yes")]
        [InlineData("OK\u007fX-Injected: yes")]
        public void Status_RejectsInvalidTextWithoutChangingCode(string statusText) {
            HttpResponse response = CreateResponse();
            response.Status(201, "Created safely");

            Assert.Throws<ArgumentException>(() => response.Status(202, statusText));
            Assert.Equal(201, response.StatusCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Bad Header")]
        [InlineData("Bad:Header")]
        [InlineData("Bad\rHeader")]
        [InlineData("Bad\nHeader")]
        [InlineData("Bad\0Header")]
        [InlineData("Héader")]
        public void AddHeader_RejectsInvalidName(string name) {
            HttpResponse response = CreateResponse();

            Assert.Throws<ArgumentException>(() => response.AddHeader(name, "value"));
        }

        [Theory]
        [InlineData("value\rX-Injected: yes")]
        [InlineData("value\nX-Injected: yes")]
        [InlineData("value\r\nX-Injected: yes")]
        [InlineData("value\0X-Injected: yes")]
        [InlineData("value\u001fX-Injected: yes")]
        [InlineData("value\u007fX-Injected: yes")]
        [InlineData("valué")]
        public void AddHeader_RejectsInvalidValue(string value) {
            HttpResponse response = CreateResponse();

            Assert.Throws<ArgumentException>(() => response.AddHeader("X-Test", value));
        }

        [Fact]
        public void RequiredMetadata_RejectsNull() {
            HttpResponse response = CreateResponse();

            Assert.Throws<ArgumentNullException>(() => response.AddHeader(null!, "value"));
            Assert.Throws<ArgumentNullException>(() => response.AddHeader("X-Test", null!));
            Assert.Throws<ArgumentNullException>(() => response.ContentType(null!));
            Assert.Throws<ArgumentNullException>(() => response.Redirect(null!));
            Assert.Throws<ArgumentNullException>(() => response.Attachment(null!));
            Assert.Throws<ArgumentNullException>(() => response.SetCookie(null!, "value"));
            Assert.Throws<ArgumentNullException>(() => response.SetCookie("sid", null!));
        }

        [Fact]
        public void RedirectContentTypeAndConnection_RejectInjectedValues() {
            const string injected = "safe\r\nX-Injected: yes";
            HttpResponse response = CreateResponse();
            response.AddHeader("Connection", "keep-alive");

            Assert.Throws<ArgumentException>(() => response.Redirect(injected));
            Assert.Equal(200, response.StatusCode);
            Assert.Throws<ArgumentException>(() => response.ContentType(injected));
            Assert.Throws<ArgumentException>(() => response.AddHeader("Content-Type", injected));
            Assert.Throws<ArgumentException>(() => response.AddHeader("Connection", injected));
            Assert.Equal("keep-alive", response.Connection);
        }

        [Fact]
        public void BodyMethods_ValidateContentTypeBeforeChangingBody() {
            const string injected = "text/plain\r\nX-Injected: yes";
            HttpResponse response = CreateResponse();
            using MemoryStream owner = new();

            try {
                response.Text("original");

                Assert.Throws<ArgumentException>(() => response.Body(Array.Empty<byte>(), injected));
                Assert.Throws<ArgumentException>(() => response.Body(ReadOnlyMemory<byte>.Empty, injected));
                Assert.Throws<ArgumentException>(() => response.Body(ReadOnlyMemory<byte>.Empty, owner, injected));
                Assert.Throws<ArgumentException>(() => response.Text("replacement", injected));
                Assert.Throws<ArgumentException>(() => response.Html("replacement", injected));
                Assert.Throws<ArgumentException>(() => response.Json(new { value = 1 }, injected));
                Assert.Throws<ArgumentException>(() => response.File(new FileInfo("unused"), injected));
            }
            finally {
                response.Reset();
            }
        }

        [Theory]
        [InlineData("bad name", "value", "/", null)]
        [InlineData("sid", "value with spaces", "/", null)]
        [InlineData("sid", "value\r\nX-Injected=yes", "/", null)]
        [InlineData("sid", "value", "/; Secure", null)]
        [InlineData("sid", "value", "/", "example.com; Secure")]
        [InlineData("sid", "value", "/\0admin", null)]
        [InlineData("sid", "value", "/", "example.com\r\nX-Injected=yes")]
        public void SetCookie_RejectsInvalidComponents(string name, string value, string? path, string? domain) {
            HttpResponse response = CreateResponse();
            HttpResponse.CookieOptions options = new(path: path, domain: domain);

            Assert.Throws<ArgumentException>(() => response.SetCookie(name, value, options));
        }

        [Fact]
        public void ValidMetadata_IsAccepted() {
            HttpResponse response = CreateResponse();
            HttpResponse redirectResponse = CreateResponse();
            HttpResponse.CookieOptions options = new(path: "/api v1", domain: ".example.com", secure: true, httpOnly: true);

            HttpResponse result = response
                .Status(299, "Custom Status")
                .AddHeader("!#$%&'*+-.^_`|~", "value with printable punctuation: ;,=/")
                .ContentType("application/json; charset=utf-8")
                .AddHeader("Connection", "keep-alive")
                .SetCookie("sid", "abc-._~%2F==", options);

            Assert.Same(response, result);
            Assert.Equal(299, response.StatusCode);
            Assert.Equal("keep-alive", response.Connection);
            Assert.Same(redirectResponse, redirectResponse.Redirect("/target?x=1&y=2"));
            Assert.Equal(302, redirectResponse.StatusCode);
        }

        [Fact]
        public async Task RejectedValues_DoNotAppearInRawResponse() {
            SimpleWServer server = new(IPAddress.Loopback, 0);
            server.MapGet("/", (HttpSession session) => {
                session.Response.Text("original");
                TryRejectedMutation(() => session.Response.AddHeader("X-Attempt", "safe\r\nX-Injected-Header: yes"));
                TryRejectedMutation(() => session.Response.Status(200, "OK\r\nX-Injected-Status: yes"));
                TryRejectedMutation(() => session.Response.Redirect("/safe\r\nX-Injected-Redirect: yes"));
                TryRejectedMutation(() => session.Response.SetCookie("sid", "safe\r\nX-Injected-Cookie=yes"));
                TryRejectedMutation(() => session.Response.Text("replacement", "text/plain\r\nX-Injected-Content-Type: yes"));

                return session.Response.AddHeader("X-Safe", "yes");
            });
            await server.StartAsync();

            try {
                using TcpClient client = new();
                await client.ConnectAsync(IPAddress.Loopback, server.Port);
                using NetworkStream stream = client.GetStream();

                byte[] request = Encoding.ASCII.GetBytes(
                    "GET / HTTP/1.1\r\n" +
                    $"Host: {server.Address}:{server.Port}\r\n" +
                    "Connection: close\r\n" +
                    "\r\n"
                );
                await stream.WriteAsync(request);

                string rawResponse = await ReadToEndAsync(stream);

                Assert.Contains("HTTP/1.1 200 OK\r\n", rawResponse);
                Assert.Contains("X-Safe: yes\r\n", rawResponse);
                Assert.EndsWith("\r\n\r\noriginal", rawResponse);
                Assert.DoesNotContain("X-Injected", rawResponse);
            }
            finally {
                await server.StopAsync();
            }
        }

        private static HttpResponse CreateResponse() => new(null!, ArrayPool<byte>.Shared);

        private static void TryRejectedMutation(Action action) {
            try {
                action();
            }
            catch (ArgumentException) {
            }
        }

        private static async Task<string> ReadToEndAsync(NetworkStream stream) {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
            StringBuilder response = new();
            byte[] buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0) {
                response.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }
            return response.ToString();
        }
    }
}
