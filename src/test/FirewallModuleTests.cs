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
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Firewall_Metadata_Should_Override_Global_AllowRules() {
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Firewall_DenyIp_Metadata_Should_Block_Handler() {
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Firewall_AllowIp_Metadata_Should_Default_Deny_When_No_Rule_Matches() {
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Firewall_RateLimit_Metadata_Should_Block_Second_Request() {
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Firewall_Metadata_Should_Combine_Controller_And_Method_Rules() {
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Firewall_DenyUnknownCountry_Metadata_Should_Block_When_Country_Is_Unresolved() {
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
        }

        [Fact]
        public async Task Firewall_UnknownCountry_Should_Not_Match_When_Disabled() {
            int port = PortManager.GetFreePort();
            var server = new SimpleWServer(IPAddress.Loopback, port);

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
                PortManager.ReleasePort(port);
            }
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
