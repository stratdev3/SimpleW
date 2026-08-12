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
