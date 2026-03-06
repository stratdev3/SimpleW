using Serilog;
using Serilog.Events;
using SimpleWLog = SimpleW.Observability;


namespace SimpleW.Helper.Serilog {

    /// <summary>
    /// Bridge SimpleW.Log sink to Serilog
    /// </summary>
    public static class SimpleWSerilogBridge {

        /// <summary>
        /// Installs a sink that forwards SimpleW logs to Serilog.
        /// Returns an IDisposable to uninstall.
        /// </summary>
        public static IDisposable Install(ILogger serilog, SimpleWLog.LogLevel simpleWMinimumLevel = SimpleWLog.LogLevel.Information, bool replaceExistingSink = false, Action<SimpleWSerilogOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(serilog);

            SimpleWSerilogOptions options = new() {
                Mode = replaceExistingSink ? SinkMode.Replace : SinkMode.Add
            };
            configure?.Invoke(options);
            SimpleWLog.LogLevel min = options.MinimumLevel ?? DetectSimpleWMinimumLevel(serilog);

            Action<SimpleWLog.LogEntry> sink = entry => {

                // SimpleW gating
                if (!SimpleWLog.Log.IsEnabledFor(entry.Level)) {
                    return;
                }

                LogEventLevel level = MapLevel(entry.Level);

                // Serilog gating (important if you keep SW min low, or have overrides)
                if (!serilog.IsEnabled(level)) {
                    return;
                }

                ILogger logger = serilog;

                if (options.IncludeSource) {
                    logger = logger.ForContext(options.SourcePropertyName, entry.Source);
                }
                if (options.IncludeTraceAndSpan && entry.HasActivity) {
                    if (!string.IsNullOrEmpty(entry.TraceIdString)) {
                        logger = logger.ForContext(options.TraceIdPropertyName, entry.TraceIdString);
                    }
                    if (!string.IsNullOrEmpty(entry.SpanIdString)) {
                        logger = logger.ForContext(options.SpanIdPropertyName, entry.SpanIdString);
                    }
                }

                if (options.Enricher != null) {
                    logger = options.Enricher(logger, entry);
                }

                logger.Write(level, entry.Exception, "{Message}", entry.Message);
            };

            if (options.Mode == SinkMode.Replace) {
                SimpleWLog.Log.SetSink(sink, min);
            }
            else {
                SimpleWLog.Log.MinimumLevel = min;
                SimpleWLog.Log.AddSink(sink);
            }

            return new Uninstaller(sink);
        }

        /// <summary>
        /// Uninstaller
        /// </summary>
        /// <param name="sink"></param>
        private sealed class Uninstaller(Action<SimpleWLog.LogEntry> sink) : IDisposable {
            public void Dispose() {
                try {
                    SimpleWLog.Log.RemoveSink(sink);
                }
                catch { }
            }
        }

        /// <summary>
        /// MapLevel
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        private static LogEventLevel MapLevel(SimpleWLog.LogLevel level) => level switch {
            SimpleWLog.LogLevel.Trace => LogEventLevel.Verbose,
            SimpleWLog.LogLevel.Debug => LogEventLevel.Debug,
            SimpleWLog.LogLevel.Information => LogEventLevel.Information,
            SimpleWLog.LogLevel.Warning => LogEventLevel.Warning,
            SimpleWLog.LogLevel.Error => LogEventLevel.Error,
            SimpleWLog.LogLevel.Fatal => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };

        /// <summary>
        /// DetectSimpleWMinimumLevel
        /// </summary>
        /// <param name="logger"></param>
        /// <returns></returns>
        private static SimpleWLog.LogLevel DetectSimpleWMinimumLevel(ILogger logger) {
            // Find the lowest enabled Serilog level, map to SW level
            if (logger.IsEnabled(LogEventLevel.Verbose)) {
                return SimpleWLog.LogLevel.Trace;
            }
            if (logger.IsEnabled(LogEventLevel.Debug)) {
                return SimpleWLog.LogLevel.Debug;
            }
            if (logger.IsEnabled(LogEventLevel.Information)) {
                return SimpleWLog.LogLevel.Information;
            }
            if (logger.IsEnabled(LogEventLevel.Warning)) {
                return SimpleWLog.LogLevel.Warning;
            }
            if (logger.IsEnabled(LogEventLevel.Error)) {
                return SimpleWLog.LogLevel.Error;
            }
            return SimpleWLog.LogLevel.Fatal;
        }

    }

    /// <summary>
    /// Options for SimpleW -> Serilog bridge.
    /// </summary>
    public sealed class SimpleWSerilogOptions {

        /// <summary>
        /// Minimum level applied to SimpleW internal logger.
        /// </summary>
        public SimpleWLog.LogLevel? MinimumLevel { get; set; } = null;

        /// <summary>
        /// Replace existing sink (SetSink) or chain (AddSink).
        /// </summary>
        public SinkMode Mode { get; set; } = SinkMode.Add;

        /// <summary>
        /// Include entry.Source as a Serilog property.
        /// </summary>
        public bool IncludeSource { get; set; } = true;

        /// <summary>
        /// Include TraceId/SpanId when Activity is present.
        /// </summary>
        public bool IncludeTraceAndSpan { get; set; } = true;

        /// <summary>
        /// Property name for Source.
        /// </summary>
        public string SourcePropertyName { get; set; } = "simplew_source";

        /// <summary>
        /// Property name for TraceId.
        /// </summary>
        public string TraceIdPropertyName { get; set; } = "trace_id";

        /// <summary>
        /// Property name for SpanId.
        /// </summary>
        public string SpanIdPropertyName { get; set; } = "span_id";

        /// <summary>
        /// Optional user hook to enrich the logger per entry (add custom props).
        /// </summary>
        public Func<ILogger, SimpleWLog.LogEntry, ILogger>? Enricher { get; set; }

    }

    /// <summary>
    /// SinkMode
    /// </summary>
    public enum SinkMode {
        /// <summary>
        /// Call Log.SetSink(...) (replace existing sink).
        /// </summary>
        Replace,
        /// <summary>
        /// Call Log.AddSink(...) (chain).
        /// </summary>
        Add
    }

}