using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;


namespace SimpleW.Observability {

    /// <summary>
    /// Telemetry
    /// </summary>
    public class Telemetry : IDisposable {

        /// <summary>
        /// Global Switch for Telemetry
        /// </summary>
        internal bool Enabled { get; private set; }

        /// <summary>
        /// Enable Telemetry
        /// </summary>
        internal void Enable() => Enabled = true;

        /// <summary>
        /// Disable Telemetry
        /// </summary>
        internal void Disable() => Enabled = false;

        /// <summary>
        /// Options
        /// </summary>
        public readonly TelemetryOptions Options;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="options"></param>
        public Telemetry(TelemetryOptions options) {
            Options = options;

            // trace provider
            ActivitySourceName = Options.InstanceName;
            ActivitySource = new(ActivitySourceName, Assembly.GetExecutingAssembly().GetName().Version?.ToString());

            // meter provider
            MeterName = Options.InstanceName;
            Meter = new(MeterName);

            // meters
            RequestsTotal = Meter.CreateCounter<long>("http.server.request.count", unit: "request");
            RequestDurationMs = Meter.CreateHistogram<double>("http.server.request.duration", unit: "ms");
            ResponsesTotal = Meter.CreateCounter<long>("http.server.response.count", unit: "response");
            ResponseDurationMs = Meter.CreateHistogram<double>("http.server.response.duration", unit: "ms");
            ActiveSessions = Meter.CreateObservableGauge<long>("simplew.session.active", () => Volatile.Read(ref _activeSessions), unit: "session");
        }

        #region traces

        /// <summary>
        /// Activity Source Name
        /// </summary>
        private readonly string ActivitySourceName;

        /// <summary>
        /// Activity Source
        /// </summary>
        private readonly ActivitySource ActivitySource;

        /// <summary>
        /// Start an Activity with DisplayName else Request Method/Path
        /// </summary>
        /// <param name="session"></param>
        /// <param name="displayName"></param>
        /// <param name="useRequest"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Activity? StartActivity(HttpSession session, string? displayName = null, bool useRequest = true) {
            Activity? activity = ActivitySource.StartActivity(displayName ?? $"{session.Request.Method} {session.Request.Path}", ActivityKind.Server);

            if (activity != null) {

                activity.SetTag("simplew.instance", Options.InstanceId);
                activity.SetTag("session_id", session.Id.ToString());

                activity.SetTag("network.transport", "TCP");
                activity.SetTag("network.type", "ipv4");

                if (useRequest) {
                    activity.SetTag("url.path", session.Request.Path);
                    activity.SetTag("url.query", session.Request.QueryString);
                    activity.SetTag("url.scheme", (session.IsSsl ? "https" : "http"));
                    activity.SetTag("url.host", session.Request.Headers.Host);

                    //activity.SetTag("http.route", request.Uri.AbsolutePath);
                    activity.SetTag("http.request.method", session.Request.Method);
                    activity.SetTag("http.request.path", session.Request.Path);
                    //activity.SetTag("http.request.route", request.Uri.AbsolutePath);
                    activity.SetTag("http.request.body.size", session.Request.Headers.ContentLengthRaw);

                    activity.SetTag("client.address", (session.Socket?.RemoteEndPoint as IPEndPoint)?.Address.ToString());
                    activity.SetTag("user_agent.original", session.Request.Headers.UserAgent);

                    string? login = session.Request?.User?.Login;
                    if (login != null) {
                        activity.SetTag("user.id", session.Request?.User?.Id);
                        activity.SetTag("user.login", session.Request?.User?.Login);
                    }
                }
            }

            return activity;
        }

        /// <summary>
        /// Stop an Activity
        /// </summary>
        /// <param name="activity"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void StopActivity(Activity? activity) {
            activity?.Dispose();
        }

        /// <summary>
        /// Update Activity Add Exception
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="ex"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateActivityAddException(Activity? activity, Exception ex) {
            if (activity == null || !Options.RecordException) {
                return;
            }

            activity.SetStatus(ActivityStatusCode.Error, ex.Message);

            ActivityTagsCollection tags = new();
            tags.Add("exception.type", ex.GetType().FullName);
            if (Options.IncludeStackTrace) {
                tags.Add("exception.stacktrace", ex.ToString());
            }
            activity.AddEvent(new ActivityEvent(ex.Message, default, tags));
        }

        /// <summary>
        /// Update Activity When No Response
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="session"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateActivityAddNoResponse(Activity? activity, HttpSession session) {
            if (activity == null) {
                return;
            }

            activity.DisplayName = $"{session.Request.Method} {session.Request.RouteTemplate}";
            activity.SetTag("http.route", session.Request.RouteTemplate);
            activity.SetTag("http.target", session.Request.Path);

            activity.SetStatus(ActivityStatusCode.Error, "Response Not Sent");

            activity.SetTag("http.response.sent", false);
            activity.SetTag("http.response.status_code", 0);
        }

        /// <summary>
        /// Update Activity Add Response
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="session"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdateActivityAddResponse(Activity? activity, HttpSession session) {
            if (activity == null) {
                return;
            }

            activity.DisplayName = $"{session.Request.Method} {session.Request.RouteTemplate}";
            activity.SetTag("http.route", session.Request.RouteTemplate);

            // opentelemetry convention indicate when status code not in (1xx, 2xx, 3xx)
            // the span status need to be set to "Error"
            // source : https://opentelemetry.io/docs/specs/otel/trace/semantic_conventions/http/
            if (session.Response.StatusCode >= 400) {
                activity.SetStatus(ActivityStatusCode.Error, "StatusCode");
            }

            activity.SetTag("http.response.sent", true);
            activity.SetTag("http.response.status_code", session.Response.StatusCode);
            activity.SetTag("http.response.size", session.Response.BytesSent);

            Options.EnrichWithHttpSession?.Invoke(activity, session);
        }

        #endregion traces

        #region meter

        /// <summary>
        /// Meter Name
        /// </summary>
        private readonly string MeterName;

        /// <summary>
        /// Meter
        /// </summary>
        public readonly Meter Meter;

        /// <summary>
        /// RequestsTotal
        /// </summary>
        private readonly Counter<long> RequestsTotal;

        /// <summary>
        /// RequestDurationMs
        /// </summary>
        private readonly Histogram<double> RequestDurationMs;

        /// <summary>
        /// ResponseTotal
        /// </summary>
        private readonly Counter<long> ResponsesTotal;

        /// <summary>
        /// ResponseDurationMs
        /// </summary>
        private readonly Histogram<double> ResponseDurationMs;

        /// <summary>
        /// ActiveSessions
        /// </summary>
        private readonly ObservableGauge<long> ActiveSessions;

        /// <summary>
        /// AddRequestMetrics
        /// </summary>
        /// <param name="session"></param>
        /// <param name="elapsedMs"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddRequestMetrics(HttpSession session, double elapsedMs) {
            RequestsTotal.Add(
                1,
                new("simplew.instance", Options.InstanceId),
                new("http.method", session.Request.Method),
                new("http.route", session.Request.RouteTemplate ?? "unmatched")
            );

            RequestDurationMs.Record(
                elapsedMs,
                new("simplew.instance", Options.InstanceId),
                new("http.method", session.Request.Method),
                new("http.route", session.Request.RouteTemplate ?? "unmatched")
            );
        }

        /// <summary>
        /// AddResponseMetrics
        /// </summary>
        /// <param name="session"></param>
        /// <param name="elapsedMs"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddResponseMetrics(HttpSession session, double elapsedMs) {
            ResponsesTotal.Add(
                1,
                new("simplew.instance", Options.InstanceId),
                new("http.method", session.Request.Method),
                new("http.route", session.Request.RouteTemplate ?? "unmatched"),
                new("http.response.status_code", session.Response.StatusCode)
            );

            ResponseDurationMs.Record(
                elapsedMs,
                new("simplew.instance", Options.InstanceId),
                new("http.method", session.Request.Method),
                new("http.route", session.Request.RouteTemplate ?? "unmatched"),
                new("http.response.status_code", session.Response.StatusCode)
            );
        }

        /// <summary>
        /// ActiveSession Count
        /// </summary>
        private long _activeSessions;

        /// <summary>
        /// Increment Session Gauge
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ActiveSessionIncrement() => Interlocked.Increment(ref _activeSessions);

        /// <summary>
        /// Decrement Session Gauge
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ActiveSessionDecrement() => Interlocked.Decrement(ref _activeSessions);

        #endregion meters

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose() {
            ActivitySource.Dispose();
            Meter.Dispose();
        }

        #region helpers

        /// <summary>
        /// Get Watch
        /// </summary>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetWatch() => Stopwatch.GetTimestamp();

        /// <summary>
        /// Calculate elapsed time in ms between two get watches
        /// </summary>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ElapsedMs(long start, long end) {
            return ((end - start) * 1000.0) / Stopwatch.Frequency;
        }

        #endregion helpers

    }

    /// <summary>
    /// Telemetry Handler
    /// </summary>
    /// <param name="activity">non null activity</param>
    /// <param name="session">full featured session</param>
    public delegate void TelemetryHandler(Activity activity, HttpSession session);

    /// <summary>
    /// Telemetry Options
    /// </summary>
    public sealed class TelemetryOptions {

        /// <summary>
        /// InstanceId
        /// </summary>
        public string? InstanceId { get; set; } = "";

        /// <summary>
        /// InstanceName
        /// </summary>
        public string InstanceName => string.IsNullOrWhiteSpace(InstanceId) ? "SimpleW" : $"SimpleW.{InstanceId}";

        /// <summary>
        /// Record Exception
        /// </summary>
        public bool RecordException { get; set; } = true;

        /// <summary>
        /// Include StackTrace
        /// </summary>
        public bool IncludeStackTrace { get; set; } = false;

        /// <summary>
        /// TelemetryHandler
        /// </summary>
        public TelemetryHandler? EnrichWithHttpSession { get; set; } = null;

    }

}