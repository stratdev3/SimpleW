using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NFluent;
using SimpleW;
using SimpleW.Helper.Hosting;
using Xunit;


namespace test {

    /// <summary>
    /// Tests for SimpleWHostApplicationBuilder.
    /// </summary>
    public class SimpleWHostApplicationBuilderTests {

        [Fact]
        public void ConfigureSimpleW_WithServices_Should_Run_Before_Classic_Callback() {
            ControllerActionExecutorFactory factory = (_, _) => static (session, resultHandler) => resultHandler(session, new { ok = true });
            bool classicCallbackSawFactory = false;

            var builder = SimpleWHost.CreateApplicationBuilder(Array.Empty<string>());
            builder.UseUrl("http://127.0.0.1:0");

            builder.ConfigureSimpleW((services, server) => {
                Check.That(services.GetRequiredService<IHostEnvironment>()).IsNotNull();
                server.UseControllerActionExecutorFactory(factory);
            });

            builder.ConfigureSimpleW(server => {
                classicCallbackSawFactory = ReferenceEquals(server.ControllerActionExecutorFactory, factory);
            });

            using IHost host = builder.Build();
            SimpleWServer server = host.Services.GetRequiredService<SimpleWServer>();

            Check.That(classicCallbackSawFactory).IsTrue();
            Check.That(server.ControllerActionExecutorFactory).IsSameReferenceAs(factory);
        }

    }

}
