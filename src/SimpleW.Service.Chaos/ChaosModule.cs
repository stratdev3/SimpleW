using SimpleW.Modules;
using SimpleW.Observability;


namespace SimpleW.Service.Chaos {

    /// <summary>
    /// Chaos Module
    /// </summary>
    internal sealed class ChaosModule : IHttpModule {

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILogger _log = new Logger<ChaosModule>();

        /// <summary>
        /// Options
        /// </summary>
        private readonly ChaosOptions _options;

        /// <summary>
        /// Random Generator
        /// </summary>
        private readonly Random _rng;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public ChaosModule(ChaosOptions options) {
            _options = (options ?? throw new ArgumentNullException(nameof(options))).ValidateAndNormalize();
            _rng = _options.Seed.HasValue ? new Random(_options.Seed.Value) : new Random();
        }

        /// <summary>
        /// Install Module in server (called by SimpleW)
        /// </summary>
        /// <param name="server"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public void Install(SimpleWServer server) {
            _log.Info("installing...");

            // Middleware = before routing -> affects everything under Prefix (API)
            server.UseMiddleware(InvokeAsync);

            _log.Info("installed");
        }

        /// <summary>
        /// Handler
        /// </summary>
        /// <param name="session"></param>
        /// <param name="next"></param>
        /// <returns></returns>
        private async ValueTask InvokeAsync(HttpSession session, Func<ValueTask> next) {
            if (!_options.Enabled) {
                await next().ConfigureAwait(false);
                return;
            }

            HttpRequest req = session.Request;

            // Path filter
            if (string.IsNullOrEmpty(req.Path) || !req.Path.StartsWith(_options.Prefix, StringComparison.Ordinal)) {
                await next().ConfigureAwait(false);
                return;
            }

            // Method filter
            if (_options.Methods is not null && !_options.Methods.Contains(req.Method)) {
                await next().ConfigureAwait(false);
                return;
            }

            // Probability gate
            if (_options.Probability <= 0 || _rng.NextDouble() >= _options.Probability) {
                await next().ConfigureAwait(false);
                return;
            }

            // Optional delay
            if (_options.MaxDelayMs > 0) {
                int delay = _options.MinDelayMs == _options.MaxDelayMs
                                ? _options.MinDelayMs
                                : _rng.Next(_options.MinDelayMs, _options.MaxDelayMs + 1);

                if (delay > 0) {
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }

            // Connection drop?
            if (_options.CloseConnectionProbability > 0 && _rng.NextDouble() < _options.CloseConnectionProbability) {
                try {

                    // No response, just bye-bye.
                    if (_options.AbortWithRst) {
                        await session.AbortConnectionAsync(reset: true).ConfigureAwait(false);
                    }
                    else {
                        session.Dispose();
                    }
                }
                catch {
                    // swallow: this is chaos, not a church
                }
                return;
            }

            // HTTP error response
            int code = _options.FixedStatusCode ?? PickWeightedStatus(_options.StatusWeights!);
            string text = HttpResponse.DefaultStatusText(code);

            // by forcing Connection header to close, then the session.CloseAfterResponse = true
            session.Response.AddHeader("Connection", "close");

            string? body = _options.BodyTemplate;
            if (!string.IsNullOrWhiteSpace(body)) {
                body = body.Replace("{code}", code.ToString())
                            .Replace("{text}", text);
                await session.Response.Status(code, text).Text(body).SendAsync().ConfigureAwait(false);
            }
            else {
                await session.Response.Status(code, text).RemoveBody().SendAsync().ConfigureAwait(false);
            }
        }

        private int PickWeightedStatus(Dictionary<int, double> weights) {
            // weights already validated (count>0, w>0)
            double total = 0;
            foreach (var w in weights.Values) {
                total += w;
            }

            // just in case of weird floating point: fallback
            if (total <= 0) {
                return 500;
            }

            double r = _rng.NextDouble() * total;
            foreach (var kv in weights) {
                r -= kv.Value;
                if (r <= 0) {
                    return kv.Key;
                }
            }

            // fallback (shouldn't happen)
            return weights.Keys.FirstOrDefault(500);
        }

    }

}
