using SimpleW;
using SimpleW.Service.LetsEncrypt;


namespace example;

internal sealed class LetsEncryptScenario : IScenario {

    public string Name => "letsencrypt";

    public string Description => "ACME HTTP-01 certificate issuance, using staging by default.";

    public string Usage => " --domains example.com[,www.example.com] --email admin@example.com [--storage path] [--https-port 4443] [--production]";

    public Task RunAsync(ExampleArguments arguments, CancellationToken cancellationToken) {
        arguments.EnsureAllowed("domains", "email", "storage", "https-port", "production");
        if (arguments.IoxideEnabled) {
            throw new ExampleCliException(
                "Scenario 'letsencrypt' cannot use '--ioxide' because ACME requires HTTP/HTTPS listener reloads and Ioxide TLS is single-start."
            );
        }

        string[] domains = arguments.Require("domains")
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (domains.Length == 0) {
            throw new ExampleCliException("Option '--domains' must contain at least one domain.");
        }

        string email = arguments.Require("email");
        string storagePath = arguments.Get("storage", Path.Combine(AppContext.BaseDirectory, "letsencrypt"))!;
        int httpsPort = arguments.GetInt32("https-port", 4443, 1, 65535);
        bool production = arguments.HasFlag("production");

        SimpleWServer server = ExampleServer.Create(arguments);
        server.UseLetsEncryptModule(options => {
            options.Domains = domains;
            options.Email = email;
            options.StoragePath = storagePath;
            options.UseStaging = !production;
            options.HttpPort = arguments.Port;
            options.HttpsPort = httpsPort;
            options.RenewBefore = TimeSpan.FromDays(30);
        });
        server.MapGet("/", () => new {
            message = "Let's Encrypt scenario",
            environment = production ? "production" : "staging",
            domains,
            httpPort = arguments.Port,
            httpsPort
        });

        if (production) {
            ExampleConsole.Warning("ACME production is enabled. Public DNS and port forwarding must already be configured.");
        }
        else {
            ExampleConsole.Info("ACME staging is enabled. Add --production only when the public setup is ready.");
        }
        return ExampleServer.RunAsync(server, "http", arguments, cancellationToken);
    }

}
