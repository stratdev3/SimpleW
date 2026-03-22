using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.Helper.Jwt;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for SimpleWServer
    /// </summary>
    public class SimpleWServerTests {

        #region constructor

        [Fact]
        public void UseAddress_Should_Update_Endpoint_Before_Start() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);

            server.UseAddress(IPAddress.Any);

            Check.That(server.Address).IsEqualTo(IPAddress.Any);
            Check.That(server.Port).IsEqualTo(5001);
        }

        [Fact]
        public void UsePort_Should_Update_Endpoint_Before_Start() {

            var server = new SimpleWServer(IPAddress.Loopback, 5001);

            server.UsePort(5002);

            Check.That(server.Address).IsEqualTo(IPAddress.Loopback);
            Check.That(server.Port).IsEqualTo(5002);
        }

        [Fact]
        public async Task UseAddress_AfterStart_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => new { ok = true });

            await server.StartAsync();

            Assert.Throws<InvalidOperationException>(() => server.UseAddress(IPAddress.Any));

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task UsePort_AfterStart_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => new { ok = true });

            await server.StartAsync();

            Assert.Throws<InvalidOperationException>(() => server.UsePort(server.Port + 1));

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion constructor

        #region result handler

        [Fact]
        public async Task ConfigureResultHandler_Should_Override_Default_Handler() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.ConfigureResultHandler((session, result) => {
                return session.Response
                              .AddHeader("X-Result-Handler", "custom")
                              .Json(result)
                              .SendAsync();
            });

            server.MapGet("/api/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/hello");
            var content = await response.Content.ReadAsStringAsync();

            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { message = "Hello World !" }));
            Check.That(response.Headers.Contains("X-Result-Handler")).IsTrue();
            Check.That(response.Headers.GetValues("X-Result-Handler").First()).IsEqualTo("custom");

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion result handler

        #region MaxRequestSize

        [Fact]
        public async Task Should_Reject_Request_When_Header_Too_Large() {

            // arrange
            var server = new SimpleWServer(IPAddress.Loopback, 0)
                .Configure(o => {
                    o.MaxRequestHeaderSize = 64; // ultra small
                });

            server.Router.MapGet("/", () => "OK");

            await server.StartAsync();

            var endpoint = (IPEndPoint)server.EndPoint;

            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);

            using var stream = client.GetStream();

            // header too big
            string request =
                "GET / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "X-Big: " + new string('A', 200) + "\r\n" +
                "\r\n";

            var bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes);

            // read response
            var buffer = new byte[1024];
            int read = await stream.ReadAsync(buffer);

            // close or error
            Assert.True(read == 0 || Encoding.ASCII.GetString(buffer, 0, read).Contains("431"));

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task Should_Reject_Request_When_Body_Too_Large() {

            // arrange
            var server = new SimpleWServer(IPAddress.Loopback, 0)
                .Configure(o => {
                    o.MaxRequestBodySize = 32; // ultra small
                });

            server.Router.MapPost("/", (HttpSession s) => "OK");

            await server.StartAsync();

            var endpoint = (IPEndPoint)server.EndPoint;

            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);

            using var stream = client.GetStream();

            string body = new string('A', 200); // too big

            string request =
                "POST / HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "\r\n" +
                body;

            var bytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(bytes);

            var buffer = new byte[1024];
            int read = await stream.ReadAsync(buffer);

            // close or erreur
            Assert.True(read == 0 || Encoding.ASCII.GetString(buffer, 0, read).Contains("413"));

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion

        #region SessionTimeout

        [Fact]
        public async Task Should_Close_Connection_When_SessionTimeout_Reached() {

            // arrange
            var server = new SimpleWServer(IPAddress.Loopback, 0)
                .Configure(o => {
                    o.SessionTimeout = TimeSpan.FromMilliseconds(200); // très court
                });

            await server.StartAsync();

            var endpoint = (IPEndPoint)server.EndPoint;

            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Address, endpoint.Port);

            using var stream = client.GetStream();

            // on attend que le timeout serveur déclenche
            await Task.Delay(500);

            // try read → doit être fermé
            var buffer = new byte[1];
            int read = 0;

            try {
                read = await stream.ReadAsync(buffer);
            }
            catch {
                // socket déjà fermée → OK
            }

            // assert : soit 0 (closed), soit exception
            Assert.True(read == 0);

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion SessionTimeout

        #region callbacks

        [Fact]
        public async Task OnStarted_Should_Invoke_Sync_And_Async_Callbacks() {

            int syncCallCount = 0;
            var asyncStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => new { ok = true });

            server.OnStarted(s => {
                Interlocked.Increment(ref syncCallCount);
                Check.That(s.IsStarted).IsTrue();
            });

            server.OnStarted(async s => {
                await Task.Yield();
                Check.That(s.IsStarted).IsTrue();
                asyncStarted.TrySetResult(true);
            });

            await server.StartAsync();
            await asyncStarted.Task;

            Check.That(syncCallCount).IsEqualTo(1);

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task OnStopped_Should_Invoke_Sync_And_Async_Callbacks() {

            int syncCallCount = 0;
            int asyncCallCount = 0;

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.MapGet("/", () => new { ok = true });

            server.OnStopped(s => {
                Interlocked.Increment(ref syncCallCount);
                Check.That(s.IsStarted).IsFalse();
                Check.That(s.IsStopping).IsFalse();
            });

            server.OnStopped(async s => {
                await Task.Yield();
                Interlocked.Increment(ref asyncCallCount);
                Check.That(s.IsStarted).IsFalse();
                Check.That(s.IsStopping).IsFalse();
            });

            await server.StartAsync();
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);

            Check.That(syncCallCount).IsEqualTo(1);
            Check.That(asyncCallCount).IsEqualTo(1);
        }

        #endregion callbacks

        #region client ip resolver

        [Fact]
        public async Task ConfigureClientIPResolver_Should_Override_ClientIpAddress() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));

            server.MapGet("/api/ip", (HttpSession session) => {
                return new {
                    ip = session.ClientIpAddress!.ToString()
                };
            });

            await server.StartAsync();

            var client = new HttpClient();
            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/ip");
            var content = await response.Content.ReadAsStringAsync();

            Check.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new {
                ip = "203.0.113.10"
            }));

            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion client ip resolver

        #region slow request protection

        [Fact]
        public async Task RequestHeadersTimeout_Should_Close_Connection_And_May_Return_408_When_Headers_Are_Too_Slow() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.Configure(options => {
                options.RequestHeadersTimeout = TimeSpan.FromMilliseconds(150);
                options.SessionTimeout = TimeSpan.FromSeconds(5);
            });

            server.MapGet("/api/test/hello", () => {
                return new { message = "Hello World !" };
            });

            await server.StartAsync();

            try {
                // client
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, server.Port);

                using NetworkStream stream = client.GetStream();

                byte[] partialRequest = Encoding.ASCII.GetBytes(
                    "GET /api/test/hello HTTP/1.1\r\n" +
                    "Host: localhost\r\n"
                );

                await stream.WriteAsync(partialRequest);
                await stream.FlushAsync();

                // on laisse volontairement expirer le timeout headers
                await Task.Delay(350);

                byte[] buffer = new byte[4096];
                int read = await stream.ReadAsync(buffer);

                string response = read > 0
                    ? Encoding.ASCII.GetString(buffer, 0, read)
                    : string.Empty;

                // asserts
                if (read == 0) {
                    // connexion fermée sans qu'on récupère la réponse 408
                    Check.That(read).IsEqualTo(0);
                }
                else {
                    // on a récupéré la réponse timeout
                    Check.That(response).Contains("408 Request Timeout");
                    Check.That(response).Contains("Request headers timeout.");
                    Check.That(response).Contains("Connection: close");
                }
            }
            finally {
                await server.StopAsync();
                PortManager.ReleasePort(server.Port);
            }
        }

        [Fact]
        public async Task MinRequestBodyDataRateBytesPerSecond_Should_Return_408_When_Body_Is_Too_Slow_After_GracePeriod() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.Configure(options => {
                options.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
                options.MinRequestBodyDataRateBytesPerSecond = 20;
                options.RequestBodyGracePeriod = TimeSpan.FromMilliseconds(150);
                options.SessionTimeout = TimeSpan.FromSeconds(5);
            });

            server.MapPost("/api/test/slow-body", (HttpSession session) => {
                return session.Response.Text("OK");
            });

            await server.StartAsync();

            // client
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.Port);

            using NetworkStream stream = client.GetStream();

            string headers =
                "POST /api/test/slow-body HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                "Content-Length: 10\r\n" +
                "\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
            await stream.FlushAsync();

            // we let the grace period
            await Task.Delay(350);

            // send only one byte, under the min rate
            await stream.WriteAsync(new byte[] { (byte)'A' });
            await stream.FlushAsync();

            byte[] buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer);

            string response = Encoding.ASCII.GetString(buffer, 0, read);

            // asserts
            Check.That(read).IsStrictlyGreaterThan(0);
            Check.That(response).Contains("408 Request Timeout");
            Check.That(response).Contains("Request body too slow.");
            Check.That(response).Contains("Connection: close");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task RequestBodyGracePeriod_Should_Allow_Slow_Start_If_Body_Finishes_Before_Rate_Check_Fails() {

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.Configure(options => {
                options.RequestHeadersTimeout = TimeSpan.FromSeconds(2);
                options.MinRequestBodyDataRateBytesPerSecond = 100;
                options.RequestBodyGracePeriod = TimeSpan.FromMilliseconds(400);
                options.SessionTimeout = TimeSpan.FromSeconds(5);
            });

            server.MapPost("/api/test/grace", (HttpSession session) => {
                return session.Response.Text(session.Request.BodyString);
            });

            await server.StartAsync();

            // client
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.Port);

            using NetworkStream stream = client.GetStream();

            string body = "hello";
            string headers =
                "POST /api/test/grace HTTP/1.1\r\n" +
                "Host: localhost\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers));
            await stream.FlushAsync();

            // start slow, but still in the grace period
            await Task.Delay(150);

            // then send all the body before simplew check
            await stream.WriteAsync(Encoding.ASCII.GetBytes(body));
            await stream.FlushAsync();

            byte[] buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer);

            string response = Encoding.ASCII.GetString(buffer, 0, read);

            // asserts
            Check.That(read).IsStrictlyGreaterThan(0);
            Check.That(response).Contains("200 OK");
            Check.That(response).Contains(body);
            Check.That(response).DoesNotContain("408 Request Timeout");

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion slow request protection

        #region principal

        [Fact]
        public async Task ConfigurePrincipalResolver_WithBearerToken_Should_Populate_SessionUser() {

            // jwt
            JwtBearerOptions jwtOptions = JwtBearerOptions.Create(
                secretKey: "super-secret-key",
                issuer: "SimpleW",
                audience: "SimpleW.Client"
            );

            HttpIdentity identity = new(
                isAuthenticated: true,
                authenticationType: "Bearer",
                identifier: "owner",
                name: "Owner",
                email: "owner@simplew.dev",
                roles: new[] { "admin", "devops" },
                properties: new[] {
                    new IdentityProperty("tenant_id", "simplew"),
                    new IdentityProperty("plan", "pro")
                }
            );

            string token = JwtBearerHelper.CreateToken(
                jwtOptions,
                identity,
                lifetime: TimeSpan.FromHours(1)
            );

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.ConfigurePrincipalResolver(session => {
                string? authorization = session.Request.Headers.Authorization;

                if (string.IsNullOrWhiteSpace(authorization)
                    || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
                    return null;
                }

                string jwt = authorization["Bearer ".Length..].Trim();

                if (!JwtBearerHelper.TryValidateToken(jwtOptions, jwt, out HttpPrincipal? principal, out string? error)) {
                    return null;
                }

                return principal;
            });

            server.MapGet("/api/me", (HttpSession session) => {
                return new {
                    isAuthenticated = session.Principal.IsAuthenticated,
                    identifier = session.Principal.Identity.Identifier,
                    name = session.Principal.Name,
                    email = session.Principal.Email,
                    isAdmin = session.Principal.IsInRole("admin"),
                    tenant = session.Principal.Get("tenant_id"),
                    plan = session.Principal.Get("plan")
                };
            });

            await server.StartAsync();

            // client
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/me");
            var content = await response.Content.ReadAsStringAsync();

            // asserts
            Check.That(response.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content).IsEqualTo(JsonSerializer.Serialize(new {
                isAuthenticated = true,
                identifier = "owner",
                name = "Owner",
                email = "owner@simplew.dev",
                isAdmin = true,
                tenant = "simplew",
                plan = "pro"
            }));

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public async Task ConfigurePrincipalResolver_Should_Be_Lazy_Until_SessionUser_IsRead() {

            int resolverCallCount = 0;

            // server
            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            server.ConfigurePrincipalResolver(session => {
                Interlocked.Increment(ref resolverCallCount);

                return new HttpPrincipal(new HttpIdentity(
                    isAuthenticated: true,
                    authenticationType: "Test",
                    identifier: "lazy-user",
                    name: "Lazy User",
                    email: null,
                    roles: new[] { "admin" },
                    properties: new[] {
                        new IdentityProperty("tenant_id", "simplew")
                    }
                ));
            });

            // route that does NOT read session.User
            server.MapGet("/api/no-user", () => {
                return new { ok = true };
            });

            // route that DOES read session.User
            server.MapGet("/api/with-user", (HttpSession session) => {
                return new {
                    isAuthenticated = session.Principal.IsAuthenticated,
                    identifier = session.Principal.Identity.Identifier,
                    tenant = session.Principal.Get("tenant_id")
                };
            });

            await server.StartAsync();

            var client = new HttpClient();

            // 1) no access to session.User => resolver should not be called
            var response1 = await client.GetAsync($"http://{server.Address}:{server.Port}/api/no-user");
            var content1 = await response1.Content.ReadAsStringAsync();

            Check.That(response1.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content1).IsEqualTo(JsonSerializer.Serialize(new { ok = true }));
            Check.That(resolverCallCount).IsEqualTo(0);

            // 2) access to session.User => resolver should be called exactly once for this request
            var response2 = await client.GetAsync($"http://{server.Address}:{server.Port}/api/with-user");
            var content2 = await response2.Content.ReadAsStringAsync();

            Check.That(response2.StatusCode).Is(HttpStatusCode.OK);
            Check.That(content2).IsEqualTo(JsonSerializer.Serialize(new {
                isAuthenticated = true,
                identifier = "lazy-user",
                tenant = "simplew"
            }));
            Check.That(resolverCallCount).IsEqualTo(1);

            // dispose
            await server.StopAsync();
            PortManager.ReleasePort(server.Port);
        }

        #endregion principal

        #region telemetry

        [Fact]
        public void EnableTelemetry_And_DisableTelemetry_Should_Update_Status() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());

            Check.That(server.IsTelemetryEnabled).IsFalse();

            server.EnableTelemetry();
            Check.That(server.IsTelemetryEnabled).IsTrue();

            server.DisableTelemetry();
            Check.That(server.IsTelemetryEnabled).IsFalse();

            PortManager.ReleasePort(server.Port);
        }

        [Fact]
        public void ConfigureTelemetry_After_EnableTelemetry_Should_Throw() {

            var server = new SimpleWServer(IPAddress.Loopback, PortManager.GetFreePort());
            server.EnableTelemetry();

            Assert.Throws<InvalidOperationException>(() => {
                server.ConfigureTelemetry(options => {
                    options.IncludeStackTrace = true;
                });
            });

            server.DisableTelemetry();
            PortManager.ReleasePort(server.Port);
        }

        #endregion telemetry

    }

}
