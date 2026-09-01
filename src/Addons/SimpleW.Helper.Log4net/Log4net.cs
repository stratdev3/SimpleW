using log4net;
using log4net.Core;
using SimpleWLog = SimpleW.Observability;


namespace SimpleW.Helper.Log4net {

    /// <summary>
    /// Bridge SimpleW.Log sink to log4net.
    /// </summary>
    public static class SimpleWLog4NetBridge {

        /// <summary>
        /// Boundary type used by log4net when building location information.
        /// </summary>
        private static readonly Type _callerStackBoundaryDeclaringType = typeof(SimpleWLog4NetBridge);

        /// <summary>
        /// Installs a sink that forwards SimpleW logs to log4net.
        /// Returns an IDisposable to uninstall.
        /// </summary>
        public static IDisposable Install(ILog log4net, bool replaceExistingSink = false, Action<SimpleWLog4NetOptions>? configure = null) {
            ArgumentNullException.ThrowIfNull(log4net);

            SimpleWLog4NetOptions options = new() {
                Mode = replaceExistingSink ? SinkMode.Replace : SinkMode.Add
            };
            configure?.Invoke(options);

            SimpleWLog.LogLevel min = options.MinimumLevel ?? DetectSimpleWMinimumLevel(log4net);

            Action<SimpleWLog.LogEntry> sink = entry => {

                // SimpleW gating
                if (!SimpleWLog.Log.IsEnabledFor(entry.Level)) {
                    return;
                }

                ILog logger = options.LoggerResolver?.Invoke(entry) ?? log4net;
                Level level = MapLevel(entry.Level);

                // log4net gating
                if (!logger.Logger.IsEnabledFor(level)) {
                    return;
                }

                Dictionary<string, object?> properties = new(StringComparer.Ordinal);

                if (options.IncludeSource) {
                    properties[options.SourcePropertyName] = entry.Source;
                }
                if (options.IncludeTraceAndSpan && entry.HasActivity) {
                    if (!string.IsNullOrEmpty(entry.TraceIdString)) {
                        properties[options.TraceIdPropertyName] = entry.TraceIdString;
                    }
                    if (!string.IsNullOrEmpty(entry.SpanIdString)) {
                        properties[options.SpanIdPropertyName] = entry.SpanIdString;
                    }
                }

                options.Enricher?.Invoke(properties, entry);

                using (TemporaryLogicalThreadContextProperties.Apply(properties)) {
                    logger.Logger.Log(_callerStackBoundaryDeclaringType, level, entry.Message, entry.Exception);
                }
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
        private sealed class Uninstaller(Action<SimpleWLog.LogEntry> sink) : IDisposable {
            public void Dispose() {
                try {
                    SimpleWLog.Log.RemoveSink(sink);
                }
                catch { }
            }
        }

        /// <summary>
        /// Map SimpleW level to log4net level.
        /// </summary>
        private static Level MapLevel(SimpleWLog.LogLevel level) => level switch {
            SimpleWLog.LogLevel.Trace => Level.Trace,
            SimpleWLog.LogLevel.Debug => Level.Debug,
            SimpleWLog.LogLevel.Information => Level.Info,
            SimpleWLog.LogLevel.Warning => Level.Warn,
            SimpleWLog.LogLevel.Error => Level.Error,
            SimpleWLog.LogLevel.Fatal => Level.Fatal,
            _ => Level.Info
        };

        /// <summary>
        /// Detect the lowest enabled log4net level and map it to SimpleW.
        /// </summary>
        private static SimpleWLog.LogLevel DetectSimpleWMinimumLevel(ILog logger) {
            if (logger.Logger.IsEnabledFor(Level.Trace)) {
                return SimpleWLog.LogLevel.Trace;
            }
            if (logger.IsDebugEnabled) {
                return SimpleWLog.LogLevel.Debug;
            }
            if (logger.IsInfoEnabled) {
                return SimpleWLog.LogLevel.Information;
            }
            if (logger.IsWarnEnabled) {
                return SimpleWLog.LogLevel.Warning;
            }
            if (logger.IsErrorEnabled) {
                return SimpleWLog.LogLevel.Error;
            }
            return SimpleWLog.LogLevel.Fatal;
        }

        /// <summary>
        /// Temporary logical thread context properties scope.
        /// </summary>
        private sealed class TemporaryLogicalThreadContextProperties : IDisposable {
            private readonly List<(string Key, object? PreviousValue, bool HadPreviousValue)> _restore = new();

            private TemporaryLogicalThreadContextProperties() { }

            public static TemporaryLogicalThreadContextProperties Apply(IReadOnlyDictionary<string, object?> properties) {
                TemporaryLogicalThreadContextProperties scope = new();

                foreach (KeyValuePair<string, object?> kv in properties) {
                    object? previous = LogicalThreadContext.Properties[kv.Key];
                    bool hadPrevious = previous != null;
                    scope._restore.Add((kv.Key, previous, hadPrevious));
                    LogicalThreadContext.Properties[kv.Key] = kv.Value;
                }

                return scope;
            }

            public void Dispose() {
                for (int i = _restore.Count - 1; i >= 0; i--) {
                    (string key, object? previousValue, bool hadPreviousValue) = _restore[i];
                    if (hadPreviousValue) {
                        LogicalThreadContext.Properties[key] = previousValue;
                    }
                    else {
                        LogicalThreadContext.Properties.Remove(key);
                    }
                }
            }
        }

    }

    /// <summary>
    /// Options for SimpleW -&gt; log4net bridge.
    /// </summary>
    public sealed class SimpleWLog4NetOptions {

        /// <summary>
        /// Minimum level applied to SimpleW internal logger.
        /// Null means auto-detect from the target log4net logger.
        /// </summary>
        public SimpleWLog.LogLevel? MinimumLevel { get; set; } = null;

        /// <summary>
        /// Replace existing sink (SetSink) or chain (AddSink).
        /// </summary>
        public SinkMode Mode { get; set; } = SinkMode.Add;

        /// <summary>
        /// Include entry.Source as a log4net context property.
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
        /// Optional hook to select a specific log4net logger per entry.
        /// By default the logger passed to Install(...) is reused.
        /// </summary>
        public Func<SimpleWLog.LogEntry, ILog>? LoggerResolver { get; set; }

        /// <summary>
        /// Optional user hook to add custom logical thread context properties per entry.
        /// </summary>
        public Action<IDictionary<string, object?>, SimpleWLog.LogEntry>? Enricher { get; set; }

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
