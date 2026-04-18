using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;


namespace SimpleW.Helper.DependencyInjection {

    /// <summary>
    /// Dependency injection extensions for SimpleW.
    /// </summary>
    public static class SimpleWDependencyInjectionExtensions {

        private const string RequestServicesBagKey = "SimpleW.Helper.DependencyInjection.RequestServices";
        private static readonly ConditionalWeakTable<SimpleWServer, DependencyInjectionState> _states = new();

        /// <summary>
        /// Enables per-request dependency injection scopes using a fixed root service provider.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="rootProvider"></param>
        /// <returns></returns>
        public static SimpleWServer UseDependencyInjection(this SimpleWServer server, IServiceProvider rootProvider) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(rootProvider);

            return server.UseDependencyInjection(() => rootProvider);
        }

        /// <summary>
        /// Enables per-request dependency injection scopes using a deferred root service provider accessor.
        /// </summary>
        /// <param name="server"></param>
        /// <param name="rootProviderAccessor"></param>
        /// <returns></returns>
        public static SimpleWServer UseDependencyInjection(this SimpleWServer server, Func<IServiceProvider> rootProviderAccessor) {
            ArgumentNullException.ThrowIfNull(server);
            ArgumentNullException.ThrowIfNull(rootProviderAccessor);

            if (server.IsStarted) {
                throw new InvalidOperationException("Dependency injection must be configured before starting the server.");
            }

            DependencyInjectionState state = _states.GetValue(server, static _ => new DependencyInjectionState());
            state.SetRootProviderAccessor(rootProviderAccessor);

            server.UseControllerActionExecutorFactory(DependencyInjectionRouteExecutorFactory.Create);
            EnsureInstalled(server, state);
            return server;
        }

        /// <summary>
        /// Returns the current request service provider.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public static IServiceProvider GetRequestServices(this HttpSession session) {
            ArgumentNullException.ThrowIfNull(session);

            if (session.Bag.TryGet<IServiceProvider>(RequestServicesBagKey, out IServiceProvider? services) && services != null) {
                return services;
            }

            throw new InvalidOperationException("Request services are not available. Call UseDependencyInjection(...) before handling requests.");
        }

        /// <summary>
        /// Returns the current request service provider.
        /// </summary>
        /// <param name="controller"></param>
        /// <returns></returns>
        public static IServiceProvider GetRequestServices(this Controller controller) {
            ArgumentNullException.ThrowIfNull(controller);
            return controller.Session.GetRequestServices();
        }

        /// <summary>
        /// Attempts to get the current request service provider.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="services"></param>
        /// <returns></returns>
        public static bool TryGetRequestServices(this HttpSession session, out IServiceProvider? services) {
            ArgumentNullException.ThrowIfNull(session);
            return session.Bag.TryGet(RequestServicesBagKey, out services) && services != null;
        }

        /// <summary>
        /// Attempts to get the current request service provider.
        /// </summary>
        /// <param name="controller"></param>
        /// <param name="services"></param>
        /// <returns></returns>
        public static bool TryGetRequestServices(this Controller controller, out IServiceProvider? services) {
            ArgumentNullException.ThrowIfNull(controller);
            return controller.Session.TryGetRequestServices(out services);
        }

        /// <summary>
        /// Resolves a request-scoped service or returns null when missing.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="session"></param>
        /// <returns></returns>
        public static T? GetRequestService<T>(this HttpSession session) where T : class {
            ArgumentNullException.ThrowIfNull(session);
            return session.GetRequestServices().GetService<T>();
        }

        /// <summary>
        /// Resolves a request-scoped service or returns null when missing.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="controller"></param>
        /// <returns></returns>
        public static T? GetRequestService<T>(this Controller controller) where T : class {
            ArgumentNullException.ThrowIfNull(controller);
            return controller.Session.GetRequestService<T>();
        }

        /// <summary>
        /// Resolves a required request-scoped service.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="session"></param>
        /// <returns></returns>
        public static T GetRequiredRequestService<T>(this HttpSession session) where T : notnull {
            ArgumentNullException.ThrowIfNull(session);
            return session.GetRequestServices().GetRequiredService<T>();
        }

        /// <summary>
        /// Resolves a required request-scoped service.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="controller"></param>
        /// <returns></returns>
        public static T GetRequiredRequestService<T>(this Controller controller) where T : notnull {
            ArgumentNullException.ThrowIfNull(controller);
            return controller.Session.GetRequiredRequestService<T>();
        }

        private static void EnsureInstalled(SimpleWServer server, DependencyInjectionState state) {
            lock (state.SyncRoot) {
                if (state.IsInstalled) {
                    return;
                }

                state.IsInstalled = true;
            }

            server.UseMiddleware((session, next) => MiddlewareAsync(session, next, state));
        }

        private static async ValueTask MiddlewareAsync(HttpSession session, Func<ValueTask> next, DependencyInjectionState state) {
            IServiceProvider rootProvider = state.GetRootProvider();

            await using AsyncServiceScope scope = rootProvider.CreateAsyncScope();
            session.Bag.Set(RequestServicesBagKey, scope.ServiceProvider);

            try {
                await next().ConfigureAwait(false);
            }
            finally {
                session.Bag.Remove(RequestServicesBagKey);
            }
        }

        /// <summary>
        /// Per-server DI state.
        /// </summary>
        private sealed class DependencyInjectionState {

            public object SyncRoot { get; } = new();
            public bool IsInstalled { get; set; }

            private Func<IServiceProvider>? _rootProviderAccessor;

            public void SetRootProviderAccessor(Func<IServiceProvider> rootProviderAccessor) {
                _rootProviderAccessor = rootProviderAccessor ?? throw new ArgumentNullException(nameof(rootProviderAccessor));
            }

            public IServiceProvider GetRootProvider() {
                Func<IServiceProvider>? accessor = _rootProviderAccessor;
                if (accessor == null) {
                    throw new InvalidOperationException("Dependency injection is not configured for this server.");
                }

                return accessor() ?? throw new InvalidOperationException("The configured root service provider accessor returned null.");
            }

        }

    }

}
