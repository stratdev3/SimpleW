using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimpleW.Observability;


namespace SimpleW.Helper.Hosting {

    /// <summary>
    /// Forward SimpleW logs to Microsoft.Extensions.Logging
    /// </summary>
    internal sealed class SimpleWMicrosoftLoggerBridge : IDisposable {

        /// <summary>
        /// Logger
        /// </summary>
        private readonly ILoggerFactory _loggerFactory;

        /// <summary>
        /// Cache
        /// </summary>
        private readonly ConcurrentDictionary<string, Microsoft.Extensions.Logging.ILogger> _cache = new(StringComparer.Ordinal);

        /// <summary>
        /// Sink
        /// </summary>
        private readonly Action<LogEntry> _sink;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="loggerFactory"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public SimpleWMicrosoftLoggerBridge(ILoggerFactory loggerFactory) {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            Log.MinimumLevel = GetMicrosoftMinimumLevel();
            _sink = Write;
            Log.AddSink(_sink);
        }

        /// <summary>
        /// Write
        /// </summary>
        /// <param name="entry"></param>
        private void Write(LogEntry entry) {
            Microsoft.Extensions.Logging.ILogger logger = _cache.GetOrAdd(
                entry.Source,
                static (source, factory) => factory.CreateLogger(source),
                _loggerFactory
            );

            logger.Log(
                Map(entry.Level),
                eventId: default,
                state: entry,
                exception: entry.Exception,
                formatter: static (state, _) => state.Message
            );
        }

        /// <summary>
        /// Read the effective Microsoft minimum level
        /// </summary>
        /// <returns></returns>
        private SimpleW.Observability.LogLevel GetMicrosoftMinimumLevel() {
            Microsoft.Extensions.Logging.ILogger logger = _loggerFactory.CreateLogger("SimpleW");

            if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Trace)) {
                return SimpleW.Observability.LogLevel.Trace;
            }
            if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug)) {
                return SimpleW.Observability.LogLevel.Debug;
            }
            if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information)) {
                return SimpleW.Observability.LogLevel.Information;
            }
            if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning)) {
                return SimpleW.Observability.LogLevel.Warning;
            }
            if (logger.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error)) {
                return SimpleW.Observability.LogLevel.Error;
            }

            return SimpleW.Observability.LogLevel.Fatal;
        }

        /// <summary>
        /// Map Levels
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        private static Microsoft.Extensions.Logging.LogLevel Map(SimpleW.Observability.LogLevel level) => level switch {
            SimpleW.Observability.LogLevel.Trace => Microsoft.Extensions.Logging.LogLevel.Trace,
            SimpleW.Observability.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
            SimpleW.Observability.LogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
            SimpleW.Observability.LogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
            SimpleW.Observability.LogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
            SimpleW.Observability.LogLevel.Fatal => Microsoft.Extensions.Logging.LogLevel.Critical,
            _ => Microsoft.Extensions.Logging.LogLevel.Information
        };

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            Log.RemoveSink(_sink);
        }

    }

    /// <summary>
    /// SimpleWHostApplicationBuilderMicrosoftLoggingExtensions
    /// </summary>
    public static class SimpleWHostApplicationBuilderMicrosoftLoggingExtensions {

        /// <summary>
        /// UseMicrosoftLogging
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static SimpleWHostApplicationBuilder UseMicrosoftLogging(this SimpleWHostApplicationBuilder builder) {
            builder.Services.AddSingleton<SimpleWMicrosoftLoggerBridge>();

            builder.Services.AddHostedService(sp => {
                SimpleWMicrosoftLoggerBridge bridge = sp.GetRequiredService<SimpleWMicrosoftLoggerBridge>();
                return new BridgeHostedService(bridge);
            });

            return builder;
        }

        /// <summary>
        /// BridgeHostedService
        /// </summary>
        private sealed class BridgeHostedService : IHostedService {

            /// <summary>
            /// Bridge
            /// </summary>
            private readonly SimpleWMicrosoftLoggerBridge _bridge;

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="bridge"></param>
            public BridgeHostedService(SimpleWMicrosoftLoggerBridge bridge) {
                _bridge = bridge;
            }

            /// <summary>
            /// Start
            /// </summary>
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            /// <summary>
            /// Stop
            /// </summary>
            /// <param name="cancellationToken"></param>
            /// <returns></returns>
            public Task StopAsync(CancellationToken cancellationToken) {
                _bridge.Dispose();
                return Task.CompletedTask;
            }

        }

    }

}
