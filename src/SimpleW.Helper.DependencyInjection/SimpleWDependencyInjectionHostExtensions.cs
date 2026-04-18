using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace SimpleW.Helper.DependencyInjection {

    /// <summary>
    /// Hosting helpers for SimpleW dependency injection.
    /// </summary>
    public static class SimpleWDependencyInjectionHostExtensions {

        /// <summary>
        /// Enables request-scoped dependency injection on a built host that contains a SimpleW server.
        /// Prefer SimpleWHostApplicationBuilder.ConfigureSimpleW((services, server) => ...)
        /// when controller routes must be mapped with DI enabled before the host is built.
        /// </summary>
        /// <param name="host"></param>
        /// <returns></returns>
        public static IHost UseSimpleWDependencyInjection(this IHost host) {
            ArgumentNullException.ThrowIfNull(host);

            SimpleWServer server = host.Services.GetRequiredService<SimpleWServer>();
            server.UseDependencyInjection(host.Services);

            return host;
        }

    }

}
