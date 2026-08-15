using System.Net;
using System.Text.Json;
using NFluent;
using SimpleW;
using SimpleW.Service.Firewall;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for FirewallModule and firewall handler metadata.
    /// </summary>
    public class FirewallModuleTests {

        [Fact]
        public void GetFirewall_Without_Module_Should_Throw() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => server.GetFirewall());

            Check.That(exception.Message).Contains("UseFirewallModule");
        }

        [Fact]
        public async Task GetFirewall_Should_Return_Same_Instance_From_Server_Session_And_Controller() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.UseFirewallModule();
            server.MapGet("/firewall/instance", (HttpSession session) => new {
                same = ReferenceEquals(server.GetFirewall(), session.GetFirewall())
            });
            server.MapController<FirewallAccessorController>("/api");

            IFirewall firewall = server.GetFirewall();
            Check.That(ReferenceEquals(firewall, server.GetFirewall())).IsTrue();

            try {
                await server.StartAsync();

                using HttpClient client = new();
                string sessionResult = await client.GetStringAsync($"http://{server.Address}:{server.Port}/firewall/instance");
                string controllerResult = await client.GetStringAsync($"http://{server.Address}:{server.Port}/api/firewall-accessor/instance");

                Check.That(sessionResult).IsEqualTo(JsonSerializer.Serialize(new { same = true }));
                Check.That(controllerResult).IsEqualTo(JsonSerializer.Serialize(new { same = true }));
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_Update_Should_Add_And_Remove_DenyRule_After_Server_Start() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);
            IpRule denyRule = IpRule.Single("203.0.113.10");

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule();
            server.MapGet("/firewall/hot-update", () => new { ok = true });

            try {
                await server.StartAsync();

                using HttpClient client = new();
                string url = $"http://{server.Address}:{server.Port}/firewall/hot-update";
                HttpResponseMessage before = await client.GetAsync(url);

                server.GetFirewall().Update(options => options.DenyRules.Add(denyRule));
                HttpResponseMessage denied = await client.GetAsync(url);

                server.GetFirewall().Update(options => options.DenyRules.RemoveAll(rule => rule == denyRule));
                HttpResponseMessage after = await client.GetAsync(url);

                Check.That(before.StatusCode).Is(HttpStatusCode.OK);
                Check.That(denied.StatusCode).Is(HttpStatusCode.Forbidden);
                Check.That(after.StatusCode).Is(HttpStatusCode.OK);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_Update_Should_Preserve_Current_Configuration_And_Reject_Invalid_Replacement() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(ResolveTestClientIp);
            server.UseFirewallModule(options => {
                options.AllowRules.Add(IpRule.Single("203.0.113.10"));
            });
            server.MapGet("/firewall/preserve-update", () => new { ok = true });

            try {
                await server.StartAsync();

                IFirewall firewall = server.GetFirewall();
                firewall.Update(options => options.DenyRules.Add(IpRule.Single("198.51.100.10")));

                Assert.Throws<ArgumentException>(() => firewall.Update(options => {
                    options.DenyRules.Add(IpRule.Single("203.0.113.10"));
                    options.StateTtl = TimeSpan.Zero;
                }));
                Assert.Throws<InvalidOperationException>(() => firewall.Update(options => {
                    options.DenyRules.Add(IpRule.Single("203.0.113.10"));
                    throw new InvalidOperationException("update failed");
                }));

                using HttpClient client = new();
                string url = $"http://{server.Address}:{server.Port}/firewall/preserve-update";
                HttpResponseMessage allowed = await GetWithClientIpAsync(client, url, "203.0.113.10");
                HttpResponseMessage denied = await GetWithClientIpAsync(client, url, "198.51.100.10");
                HttpResponseMessage allowMiss = await GetWithClientIpAsync(client, url, "192.0.2.10");

                Check.That(allowed.StatusCode).Is(HttpStatusCode.OK);
                Check.That(denied.StatusCode).Is(HttpStatusCode.Forbidden);
                Check.That(allowMiss.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_Update_Should_Serialize_Concurrent_Changes() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(ResolveTestClientIp);
            server.UseFirewallModule();
            server.MapGet("/firewall/concurrent-update", () => new { ok = true });

            try {
                await server.StartAsync();

                IFirewall firewall = server.GetFirewall();
                IpRule[] rules = Enumerable.Range(1, 16)
                                           .Select(index => IpRule.Single($"203.0.113.{index}"))
                                           .ToArray();

                await Task.WhenAll(rules.Select(rule => Task.Run(() => {
                    firewall.Update(options => options.DenyRules.Add(rule));
                })));

                using HttpClient client = new();
                string url = $"http://{server.Address}:{server.Port}/firewall/concurrent-update";
                for (int index = 1; index <= rules.Length; index++) {
                    HttpResponseMessage response = await GetWithClientIpAsync(client, url, $"203.0.113.{index}");
                    Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
                }
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_Update_Should_Reset_Only_State_Affected_By_Configuration_Change() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule(options => {
                options.GlobalRateLimit = new RateLimitOptions {
                    Limit = 1,
                    Window = TimeSpan.FromMinutes(1)
                };
                options.AllowCountries.Add(CountryRule.Unknown());
            });
            server.MapGet("/firewall/state-update", () => new { ok = true });

            try {
                await server.StartAsync();

                IFirewall firewall = server.GetFirewall();
                using HttpClient client = new();
                string url = $"http://{server.Address}:{server.Port}/firewall/state-update";

                HttpResponseMessage first = await client.GetAsync(url);
                HttpResponseMessage limited = await client.GetAsync(url);
                Check.That(first.StatusCode).Is(HttpStatusCode.OK);
                Check.That(limited.StatusCode).Is((HttpStatusCode)429);
                Check.That(GetRuntimeCount(firewall, "CountryCacheCount")).IsEqualTo(1);

                firewall.Update(options => options.DenyRules.Add(IpRule.Single("198.51.100.10")));
                HttpResponseMessage stillLimited = await client.GetAsync(url);
                Check.That(stillLimited.StatusCode).Is((HttpStatusCode)429);
                Check.That(GetRuntimeCount(firewall, "CountryCacheCount")).IsEqualTo(1);

                firewall.Update(options => {
                    options.GlobalRateLimit = new RateLimitOptions {
                        Limit = 2,
                        Window = TimeSpan.FromMinutes(1)
                    };
                    options.CountryCacheTtl = TimeSpan.FromMinutes(5);
                });

                Check.That(GetRuntimeCount(firewall, "CountryCacheCount")).IsEqualTo(0);
                HttpResponseMessage afterReset = await client.GetAsync(url);
                Check.That(afterReset.StatusCode).Is(HttpStatusCode.OK);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task UseFirewallModule_Twice_Should_Throw_And_Keep_Initial_Configuration() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule();
            server.MapGet("/firewall/replace", () => new { ok = true });

            try {
                await server.StartAsync();

                using HttpClient client = new();
                string url = $"http://{server.Address}:{server.Port}/firewall/replace";
                HttpResponseMessage before = await client.GetAsync(url);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => {
                    server.UseFirewallModule(options => options.DenyRules.Add(IpRule.Single("203.0.113.10")));
                });
                HttpResponseMessage after = await client.GetAsync(url);

                Check.That(exception.Message).Contains("GetFirewall().Update");
                Check.That(before.StatusCode).Is(HttpStatusCode.OK);
                Check.That(after.StatusCode).Is(HttpStatusCode.OK);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task UseFirewallModule_Should_Apply_Global_AllowRules() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule(options => {
                options.AllowRules.Add(IpRule.Single("198.51.100.1"));
            });
            server.MapGet("/firewall/global", () => new { ok = true });

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/firewall/global");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_Metadata_Should_Override_Global_AllowRules() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule(options => {
                options.AllowRules.Add(IpRule.Single("198.51.100.1"));
            });
            server.MapController<FirewallMetadataController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall/local-allow");
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { ok = true, endpoint = "local-allow" }));
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_DenyIp_Metadata_Should_Block_Handler() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule();
            server.MapController<FirewallMetadataController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall/local-deny");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_AllowIp_Metadata_Should_Default_Deny_When_No_Rule_Matches() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule();
            server.MapController<FirewallMetadataController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall/local-allow-miss");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_RateLimit_Metadata_Should_Block_Second_Request() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule();
            server.MapController<FirewallMetadataController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage first = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall/limited");
                HttpResponseMessage second = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall/limited");

                Check.That(first.StatusCode).Is(HttpStatusCode.OK);
                Check.That(second.StatusCode).Is((HttpStatusCode)429);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_RateLimitWhitelist_Should_Bypass_Global_RateLimit() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(ResolveTestClientIp);
            server.UseFirewallModule(options => {
                options.GlobalRateLimit = new RateLimitOptions {
                    Limit = 1,
                    Window = TimeSpan.FromMinutes(1)
                };
                options.RateLimitWhitelistRules.Add(IpRule.Cidr("203.0.113.0/24"));
            });
            server.MapGet("/firewall/global-limited", () => new { ok = true });

            try {
                await server.StartAsync();

                using HttpClient client = new();
                string url = $"http://{server.Address}:{server.Port}/firewall/global-limited";
                HttpResponseMessage normalFirst = await GetWithClientIpAsync(client, url, "198.51.100.10");
                HttpResponseMessage normalSecond = await GetWithClientIpAsync(client, url, "198.51.100.10");
                HttpResponseMessage whitelistedFirst = await GetWithClientIpAsync(client, url, "203.0.113.10");
                HttpResponseMessage whitelistedSecond = await GetWithClientIpAsync(client, url, "203.0.113.10");
                HttpResponseMessage whitelistedThird = await GetWithClientIpAsync(client, url, "203.0.113.10");

                Check.That(normalFirst.StatusCode).Is(HttpStatusCode.OK);
                Check.That(normalSecond.StatusCode).Is((HttpStatusCode)429);
                Check.That(whitelistedFirst.StatusCode).Is(HttpStatusCode.OK);
                Check.That(whitelistedSecond.StatusCode).Is(HttpStatusCode.OK);
                Check.That(whitelistedThird.StatusCode).Is(HttpStatusCode.OK);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_RateLimitWhitelist_Should_Bypass_Handler_RateLimit() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule(options => {
                options.RateLimitWhitelistRules.Add(IpRule.Single("203.0.113.10"));
            });
            server.MapController<FirewallMetadataController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage first = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall/limited");
                HttpResponseMessage second = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall/limited");

                Check.That(first.StatusCode).Is(HttpStatusCode.OK);
                Check.That(second.StatusCode).Is(HttpStatusCode.OK);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_RateLimitWhitelist_Should_Not_Bypass_DenyRules() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule(options => {
                options.DenyRules.Add(IpRule.Single("203.0.113.10"));
                options.RateLimitWhitelistRules.Add(IpRule.Single("203.0.113.10"));
                options.GlobalRateLimit = new RateLimitOptions {
                    Limit = 1,
                    Window = TimeSpan.FromMinutes(1)
                };
            });
            server.MapGet("/firewall/deny-whitelisted", () => new { ok = true });

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/firewall/deny-whitelisted");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_Metadata_Should_Combine_Controller_And_Method_Rules() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule();
            server.MapController<FirewallControllerScopeController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage allowed = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall-scope/class-allow");
                HttpResponseMessage denied = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall-scope/method-deny");

                Check.That(allowed.StatusCode).Is(HttpStatusCode.OK);
                Check.That(denied.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_DenyUnknownCountry_Metadata_Should_Block_When_Country_Is_Unresolved() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule();
            server.MapController<FirewallCountryController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall-country/unknown-deny");

                Check.That(response.StatusCode).Is(HttpStatusCode.Forbidden);
            }
            finally {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Firewall_UnknownCountry_Should_Not_Match_When_Disabled() {
            var server = new SimpleWServer(IPAddress.Loopback, 0);

            server.ConfigureClientIPResolver(_ => IPAddress.Parse("203.0.113.10"));
            server.UseFirewallModule(options => {
                options.TreatUnknownCountryAsMatchable = false;
            });
            server.MapController<FirewallCountryController>("/api");

            try {
                await server.StartAsync();

                using HttpClient client = new();
                HttpResponseMessage response = await client.GetAsync($"http://{server.Address}:{server.Port}/api/firewall-country/unknown-deny");
                string content = await response.Content.ReadAsStringAsync();

                Check.That(response.StatusCode).Is(HttpStatusCode.OK);
                Check.That(content).IsEqualTo(JsonSerializer.Serialize(new { ok = true, endpoint = "unknown-deny" }));
            }
            finally {
                await server.StopAsync();
            }
        }

        private static IPAddress ResolveTestClientIp(HttpSession session) {
            if (session.Request.Headers.TryGetValue("X-Test-Client-IP", out string? value)
                && IPAddress.TryParse(value, out IPAddress? ip)) {
                return ip;
            }

            return IPAddress.Loopback;
        }

        private static async Task<HttpResponseMessage> GetWithClientIpAsync(HttpClient client, string url, string ip) {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("X-Test-Client-IP", ip);
            return await client.SendAsync(request).ConfigureAwait(false);
        }

        private static int GetRuntimeCount(IFirewall firewall, string propertyName) {
            object? value = firewall.GetType()
                                    .GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                    ?.GetValue(firewall);
            return Assert.IsType<int>(value);
        }

        [Route("/firewall-accessor")]
        public sealed class FirewallAccessorController : Controller {

            [Route("GET", "/instance")]
            public object Instance() => new {
                same = ReferenceEquals(this.GetFirewall(), Session.GetFirewall())
            };

        }

        [Route("/firewall")]
        public sealed class FirewallMetadataController : Controller {

            [FirewallAllowIp("203.0.113.10")]
            [Route("GET", "/local-allow")]
            public object LocalAllow() => new { ok = true, endpoint = "local-allow" };

            [FirewallDenyIp("203.0.113.10")]
            [Route("GET", "/local-deny")]
            public object LocalDeny() => new { ok = true, endpoint = "local-deny" };

            [FirewallAllowIp("198.51.100.10")]
            [Route("GET", "/local-allow-miss")]
            public object LocalAllowMiss() => new { ok = true, endpoint = "local-allow-miss" };

            [FirewallRateLimit(1, 1, FirewallRateLimitWindowUnit.Minutes)]
            [Route("GET", "/limited")]
            public object Limited() => new { ok = true, endpoint = "limited" };

        }

        [FirewallAllowIp("203.0.113.0/24")]
        [Route("/firewall-scope")]
        public sealed class FirewallControllerScopeController : Controller {

            [Route("GET", "/class-allow")]
            public object ClassAllow() => new { ok = true, endpoint = "class-allow" };

            [FirewallDenyIp("203.0.113.10")]
            [Route("GET", "/method-deny")]
            public object MethodDeny() => new { ok = true, endpoint = "method-deny" };

        }

        [Route("/firewall-country")]
        public sealed class FirewallCountryController : Controller {

            [FirewallDenyUnknownCountry]
            [Route("GET", "/unknown-deny")]
            public object UnknownDeny() => new { ok = true, endpoint = "unknown-deny" };

        }

    }

}
