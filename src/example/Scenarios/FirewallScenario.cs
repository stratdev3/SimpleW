using System.Net;
using SimpleW;
using SimpleW.Service.Firewall;


namespace example;

internal sealed class FirewallScenario : IScenario {

    public string Name => "firewall";

    public string Description => "Local allow/deny rules, simulated X-Real-IP resolution and rate limiting.";

    public string Usage => " [--limit 20] [--window-seconds 10]";

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("limit", "window-seconds");

        int limit = arguments.GetInt32("limit", 20, 1, 100_000);
        int windowSeconds = arguments.GetInt32("window-seconds", 10, 1, 3600);

        SimpleWServer server = ExampleServer.Create(arguments);

        server.ConfigureClientIPResolver(session => {
            if (session.Request.Headers.TryGetValue("X-Real-IP", out string? header)
                && IPAddress.TryParse(header, out IPAddress? simulatedAddress)) {
                return simulatedAddress;
            }
            return session.RemoteEndPoint is IPEndPoint endpoint ? endpoint.Address : null;
        });
        server.UseFirewallModule(options => {
            options.AllowRules.Add(IpRule.Single("127.0.0.1"));
            options.AllowRules.Add(IpRule.Single("::1"));
            options.DenyRules.Add(IpRule.Cidr("203.0.113.0/24"));
            options.GlobalRateLimit = new RateLimitOptions {
                Limit = limit,
                Window = TimeSpan.FromSeconds(windowSeconds)
            };
        });
        server.MapGet("/", (HttpSession session) => new {
            allowed = true,
            clientIp = session.ClientIpAddress?.ToString(),
            rateLimit = $"{limit} requests per {windowSeconds} seconds",
            warning = "X-Real-IP is trusted only to simulate clients in this local example."
        });

        ExampleConsole.Warning("Local simulation: send X-Real-IP: 203.0.113.10 to exercise a denied address.");
        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
