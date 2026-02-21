using System.Diagnostics;
using System.Runtime.CompilerServices;


namespace SimpleW.Observability {

    #region logger

    /// <summary>
    /// Logger implement ILogger interface
    /// <example>
    /// // instanciate
    /// private static readonly ILogger _log = Logger("MyClass");
    /// //use
    /// _log.Info("message");
    /// </example>
    /// </summary>
    public sealed class Logger : ILogger {

        /// <summary>
        /// source
        /// </summary>
        private readonly string _source;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="source"></param>
        public Logger(string source) => _source = source;

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="msgFactory"></param>
        public void Trace(Func<string> msgFactory) => Log.Trace(_source, msgFactory);

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="message"></param>
        public void Trace(string message) => Log.Trace(_source, message);

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="message"></param>
        public void Debug(string message) => Log.Debug(_source, message);

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="msgFactory"></param>
        public void Debug(Func<string> msgFactory) => Log.Debug(_source, msgFactory);

        /// <summary>
        /// Normal Operation, foods my social media infinite scroll
        /// </summary>
        /// <param name="message"></param>
        public void Info(string message) => Log.Info(_source, message);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="message"></param>
        public void Warn(string message) => Log.Warn(_source, message);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        public void Warn(string message, Exception ex) => Log.Warn(_source, message, ex);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="message"></param>
        public void Error(string message) => Log.Error(_source, message);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        public void Error(string message, Exception ex) => Log.Error(_source, message, ex);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="message"></param>
        public void Fatal(string message) => Log.Fatal(_source, message);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        public void Fatal(string message, Exception ex) => Log.Fatal(_source, message, ex);

    }

    /// <summary>
    /// Logger implement ILogger interface
    /// <example>
    /// // instanciate
    /// private static readonly ILogger _log = Logger MyClass ;
    /// //use
    /// _log.Info("message");
    /// </example>
    /// </summary>
    public sealed class Logger<T> : ILogger {

        /// <summary>
        /// source
        /// </summary>
        private static readonly string _source = typeof(T).FullName ?? typeof(T).Name;

        /// <summary>
        /// Source
        /// </summary>
        public string Source => _source;

        /// <summary>
        /// Constructor
        /// </summary>
        public Logger() { }

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="msgFactory"></param>
        public void Trace(Func<string> msgFactory) => Log.Trace(_source, msgFactory);

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="message"></param>
        public void Trace(string message) => Log.Trace(_source, message);

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="message"></param>
        public void Debug(string message) => Log.Debug(_source, message);

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="msgFactory"></param>
        public void Debug(Func<string> msgFactory) => Log.Debug(_source, msgFactory);

        /// <summary>
        /// Normal Operation, foods my social media infinite scroll
        /// </summary>
        /// <param name="message"></param>
        public void Info(string message) => Log.Info(_source, message);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="message"></param>
        public void Warn(string message) => Log.Warn(_source, message);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        public void Warn(string message, Exception ex) => Log.Warn(_source, message, ex);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="message"></param>
        public void Error(string message) => Log.Error(_source, message);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        public void Error(string message, Exception ex) => Log.Error(_source, message, ex);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="message"></param>
        public void Fatal(string message) => Log.Fatal(_source, message);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        public void Fatal(string message, Exception ex) => Log.Fatal(_source, message, ex);

    }

    /// <summary>
    /// ILogger
    /// </summary>
    public interface ILogger {

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="messageFactory"></param>
        void Trace(Func<string> messageFactory);

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="message"></param>
        void Trace(string message);

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="message"></param>
        void Debug(string message);

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="messageFactory"></param>
        void Debug(Func<string> messageFactory);

        /// <summary>
        /// Normal Operation, foods my social media infinite scroll
        /// </summary>
        /// <param name="message"></param>
        void Info(string message);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="message"></param>
        void Warn(string message);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        void Warn(string message, Exception ex);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="message"></param>
        void Error(string message);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        void Error(string message, Exception ex);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="message"></param>
        void Fatal(string message);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        void Fatal(string message, Exception ex);
    }

    #endregion logger

    #region log

    /// <summary>
    /// LogEntry
    /// </summary>
    /// <param name="TimestampUnixMs"></param>
    /// <param name="Level"></param>
    /// <param name="Source"></param>
    /// <param name="Message"></param>
    /// <param name="Exception"></param>
    /// <param name="TraceId"></param>
    /// <param name="SpanId"></param>
    public readonly record struct LogEntry(
        long TimestampUnixMs,
        LogLevel Level,
        string Source,
        string Message,
        Exception? Exception,
        ActivityTraceId TraceId = default,
        ActivitySpanId SpanId = default
    ) {

        #region enrich

        /// <summary>
        /// Timestamp
        /// </summary>
        public DateTimeOffset TimestampUtc => DateTimeOffset.FromUnixTimeMilliseconds(TimestampUnixMs);

        /// <summary>
        /// LocalTime
        /// </summary>
        public DateTimeOffset LocalTime => DateTimeOffset.FromUnixTimeMilliseconds(TimestampUnixMs).ToLocalTime();

        /// <summary>
        /// HasActivity
        /// </summary>
        public bool HasActivity => (TraceId != default || SpanId != default);

        /// <summary>
        /// TraceIdString
        /// </summary>
        public string TraceIdString => (TraceId == default ? "" : TraceId.ToString());

        /// <summary>
        /// SpanIdString
        /// </summary>
        public string SpanIdString => (SpanId == default ? "" : SpanId.ToString());

        /// <summary>
        /// LogLevel Name Length 1
        /// </summary>
        public string LevelName1Len => Level switch {
            LogLevel.Trace          => "T",
            LogLevel.Debug          => "D",
            LogLevel.Information    => "I",
            LogLevel.Warning        => "W",
            LogLevel.Error          => "E",
            LogLevel.Fatal          => "F",
            _                       => "U"
        };

        /// <summary>
        /// LogLevel Name Length 2
        /// </summary>
        public string LevelName2Len => Level switch {
            LogLevel.Trace          => "TR",
            LogLevel.Debug          => "DB",
            LogLevel.Information    => "IN",
            LogLevel.Warning        => "WN",
            LogLevel.Error          => "ER",
            LogLevel.Fatal          => "FA",
            _                       => "UN"
        };

        /// <summary>
        /// LogLevel Name Length 3
        /// </summary>
        public string LevelName3Len => Level switch {
            LogLevel.Trace          => "TRC",
            LogLevel.Debug          => "DBG",
            LogLevel.Information    => "INF",
            LogLevel.Warning        => "WRN",
            LogLevel.Error          => "ERR",
            LogLevel.Fatal          => "FTL",
            _                       => "UNK"
        };

        /// <summary>
        /// LogLevel Name Length 4
        /// </summary>
        public string LevelName4Len => Level switch {
            LogLevel.Trace          => "TRCE",
            LogLevel.Debug          => "DBUG",
            LogLevel.Information    => "INFO",
            LogLevel.Warning        => "WARN",
            LogLevel.Error          => "ERRO",
            LogLevel.Fatal          => "FATL",
            _                       => "UNKN"
        };

        /// <summary>
        /// LogLevel Name Length 5
        /// </summary>
        public string LevelName5Len => Level switch {
            LogLevel.Trace          => "TRACE",
            LogLevel.Debug          => "DEBUG",
            LogLevel.Information    => "INFOR",
            LogLevel.Warning        => "WARNG",
            LogLevel.Error          => "ERROR",
            LogLevel.Fatal          => "FATAL",
            _                       => "UNKNO"
        };

        /// <summary>
        /// LogLevel Smiley
        /// </summary>
        public string LevelSmiley => Level switch {
            LogLevel.Trace          => "🤮",
            LogLevel.Debug          => "🤔",
            LogLevel.Information    => "😴",
            LogLevel.Warning        => "🫣",
            LogLevel.Error          => "😱",
            LogLevel.Fatal          => "💀",
            _                       => "😶‍🌫️"
        };

        #endregion enrich

    }

    /// <summary>
    /// Log
    /// <example>
    /// // publish
    /// Log.Info("mylib", "message")
    /// // subscribe
    /// Log.SetSink((log) => Console.WriteLine($"{log.Timestamp:O} [{log.Level}] {log.Source} {log.Message}"));
    /// </example>
    /// </summary>
    public static class Log {

        /// <summary>
        /// Sink is plugged
        /// </summary>
        public static bool IsEnabled => _sink != null;

        /// <summary>
        /// Minimum LogLevel (read extremely often in hot paths)
        /// </summary>
        private static int _minimumLevel = (int)LogLevel.Information;

        /// <summary>
        /// Minimum LogLevel
        /// </summary>
        public static LogLevel MinimumLevel {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (LogLevel)_minimumLevel;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _minimumLevel = (int)value;
        }

        /// <summary>
        /// Fast check for a given level
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsEnabledFor(LogLevel level) => ((int)level >= _minimumLevel && _sink != null);

        #region sink

        /// <summary>
        /// _sink
        /// </summary>
        private static Action<LogEntry>? _sink;

        /// <summary>
        /// Sink
        /// </summary>
        public static Action<LogEntry>? Sink => _sink;

        /// <summary>
        /// Set Sink
        /// </summary>
        /// <param name="sink"></param>
        public static void SetSink(Action<LogEntry> sink) => _sink = sink;

        /// <summary>
        /// Set Sink
        /// </summary>
        /// <param name="sink"></param>
        /// <param name="globalMinimumLevel"></param>
        public static void SetSink(Action<LogEntry> sink, LogLevel globalMinimumLevel) {
            _sink = sink;
            MinimumLevel = globalMinimumLevel;
        }

        /// <summary>
        /// Add Sink
        /// </summary>
        /// <param name="sink"></param>
        public static void AddSink(Action<LogEntry> sink) => _sink += sink;

        /// <summary>
        /// Remove Sink
        /// </summary>
        /// <param name="sink"></param>
        public static void RemoveSink(Action<LogEntry> sink) => _sink -= sink;

        #endregion sink

        #region emit

        /// <summary>
        /// Emit Log (low level primitive)
        /// </summary>
        /// <param name="level"></param>
        /// <param name="source"></param>
        /// <param name="message"></param>
        /// <param name="ex"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Emit(LogLevel level, string source, string message, Exception? ex = null) {
            // hot-path checks first: level then sink
            if ((int)level < _minimumLevel) {
                return;
            }

            // capture sink
            Action<LogEntry>? sink = _sink;
            if (sink == null) {
                return;
            }

            Activity? a = Activity.Current;
            sink(new LogEntry(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), level, source, message, ex, a?.TraceId ?? default, a?.SpanId ?? default));
        }

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msgFactory"></param>
        public static void Trace(string src, Func<string> msgFactory) {
            // double check here to avoid calling msgFactory if no Sink
            if (!IsEnabledFor(LogLevel.Trace)) {
                return;
            }
            // msgFactory is only executed when level is enabled and a sink exists
            Emit(LogLevel.Trace, src, msgFactory());
        }

        /// <summary>
        /// You're desperately tracking something you can't debug, good luck
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        public static void Trace(string src, string msg) => Emit(LogLevel.Trace, src, msg);

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msgFactory"></param>
        public static void Debug(string src, Func<string> msgFactory) {
            // double check here to avoid calling msgFactory if no Sink
            if (!IsEnabledFor(LogLevel.Debug)) {
                return;
            }
            // msgFactory is only executed when level is enabled and a sink exists
            Emit(LogLevel.Debug, src, msgFactory());
        }

        /// <summary>
        /// It smells shit, I want maximum informations
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        public static void Debug(string src, string msg) => Emit(LogLevel.Debug, src, msg);

        /// <summary>
        /// Normal Operation, foods my social media infinite scroll
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        public static void Info(string src, string msg) => Emit(LogLevel.Information, src, msg);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        public static void Warn(string src, string msg) => Emit(LogLevel.Warning, src, msg);

        /// <summary>
        /// It's like a compiler warning, nobody take care but it should
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        /// <param name="ex"></param>
        public static void Warn(string src, string msg, Exception ex) => Emit(LogLevel.Warning, src, msg, ex);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        public static void Error(string src, string msg) => Emit(LogLevel.Error, src, msg);

        /// <summary>
        /// Prod can continue but fix that error quickly, seriously
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        /// <param name="ex"></param>
        public static void Error(string src, string msg, Exception ex) => Emit(LogLevel.Error, src, msg, ex);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        public static void Fatal(string src, string msg) => Emit(LogLevel.Fatal, src, msg);

        /// <summary>
        /// Rest In Peace
        /// </summary>
        /// <param name="src"></param>
        /// <param name="msg"></param>
        /// <param name="ex"></param>
        public static void Fatal(string src, string msg, Exception ex) => Emit(LogLevel.Fatal, src, msg, ex);

        #endregion emit

        /// <summary>
        /// Console WriteLine
        /// </summary>
        public static Action<LogEntry> ConsoleWriteLine {
            get {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                return (log) => Console.WriteLine($"{log.LocalTime:HH:mm:ss.fff} [{log.LevelSmiley}|{log.LevelName3Len}] {log.Source} {log.Message}");
            }
        }

    }

    /// <summary>
    /// LogLevel
    /// </summary>
    public enum LogLevel : byte {
        /// <summary>
        /// I want to fill the 2To SSD quickly
        /// </summary>
        Trace,
        /// <summary>
        /// I can't debug on my laptop, so let's do it in prod
        /// </summary>
        Debug,
        /// <summary>
        /// Normal operation, it's like infinite scroll on social media
        /// </summary>
        Information,
        /// <summary>
        /// I'll look into it, next monday, after my coffee
        /// </summary>
        Warning,
        /// <summary>
        /// Not critical but not warning either
        /// </summary>
        Error,
        /// <summary>
        /// Make me wake up at 3:00 AM saturday
        /// </summary>
        Fatal,
    }

    #endregion log

}
