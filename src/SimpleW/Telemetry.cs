using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;


namespace SimpleW.Observability {

    /// <summary>
    /// Telemetry Handler
    /// </summary>
    /// <param name="activity">non null activity</param>
    /// <param name="session">full featured session</param>
    public delegate void TelemetryHandler(Activity activity, HttpSession session);

    /// <summary>
    /// Telemetry
    /// </summary>
    internal static class Telemetry {

        /// <summary>
        /// Global Switch for Telemetry
        /// </summary>
        public static bool Enabled { get; private set; }

        /// <summary>
        /// Enable Telemetry
        /// </summary>
        public static void Enable() => Enabled = true;

        /// <summary>
        /// Disable Telemetry
        /// </summary>
        public static void Disable() => Enabled = false;

        /// <summary>
        /// TelemetryHandler
        /// </summary>
        public static TelemetryHandler? TelemetryHandler { get; set; }

        #region traces

        /// <summary>
        /// Activity Source Name
        /// </summary>
        public const string ActivitySourceName = "SimpleW";

        /// <summary>
        /// Activity Source
        /// </summary>
        private static readonly ActivitySource ActivitySource = new(ActivitySourceName, Assembly.GetExecutingAssembly().GetName().Version?.ToString());

        /// <summary>
        /// Start an Activity with DisplayName else Request Method/Path
        /// </summary>
        /// <param name="session"></param>
        /// <param name="displayName"></param>
        /// <param name="useRequest"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Activity? StartActivity(HttpSession session, string? displayName = null, bool useRequest = true) {
            Activity? activity = ActivitySource.StartActivity(displayName ?? $"{session.Request.Method} {session.Request.Path}", ActivityKind.Server);

            if (activity != null) {

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
                }
            }

            return activity;
        }

        /// <summary>
        /// Stop an Activity
        /// </summary>
        /// <param name="activity"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StopActivity(Activity? activity) {
            if (activity == null) {
                return;
            }
            activity.Dispose();
        }

        /// <summary>
        /// Update Activity Add Exception
        /// </summary>
        /// <param name="activity"></param>
        /// <param name="ex"></param>
        /// <param name="includeStackTrace"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateActivityAddException(Activity? activity, Exception ex, bool includeStackTrace = false) {
            if (activity == null) {
                return;
            }

            activity.SetStatus(ActivityStatusCode.Error, ex.Message);

            ActivityTagsCollection tags = new();
            tags.Add("exception.type", ex.GetType().FullName);
            if (includeStackTrace) {
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
        public static void UpdateActivityAddNoResponse(Activity? activity, HttpSession session) {
            if (activity == null) {
                return;
            }

            activity.DisplayName = $"{session.Request.Method} {session.Request.RouteTemplate}";
            activity.SetTag("http.route", session.Request.RouteTemplate);

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
        public static void UpdateActivityAddResponse(Activity? activity, HttpSession session) {
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

            TelemetryHandler?.Invoke(activity, session);
        }


        #endregion traces

        #region meter

        /// <summary>
        /// Meter Name
        /// </summary>
        public const string MeterName = "SimpleW";

        /// <summary>
        /// Meter
        /// </summary>
        public static readonly Meter Meter = new(MeterName);

        /// <summary>
        /// RequestsTotal
        /// </summary>
        //public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>("simplew_http_requests_total", unit: "request");
        public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>("http.server.request.count", unit: "request");

        /// <summary>
        /// RequestDurationMs
        /// </summary>
        public static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>("http.server.request.duration", unit: "ms");

        /// <summary>
        /// ResponseTotal
        /// </summary>
        public static readonly Counter<long> ResponsesTotal = Meter.CreateCounter<long>("http.server.response.count", unit: "response");

        /// <summary>
        /// ResponseDurationMs
        /// </summary>
        public static readonly Histogram<double> ResponseDurationMs = Meter.CreateHistogram<double>("http.server.response.duration", unit: "ms");


        /// <summary>
        /// AddRequestMetrics
        /// </summary>
        /// <param name="session"></param>
        /// <param name="elapsedMs"></param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void AddRequestMetrics(HttpSession session, double elapsedMs) {
            RequestsTotal.Add(
                1,
                new("http.method", session.Request.Method),
                new("http.route", session.Request.RouteTemplate ?? "unmatched")
            );

            RequestDurationMs.Record(
                elapsedMs,
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
        public static void AddResponseMetrics(HttpSession session, double elapsedMs) {
            ResponsesTotal.Add(
                1,
                new("http.method", session.Request.Method),
                new("http.route", session.Request.RouteTemplate ?? "unmatched"),
                new("http.response.status_code", session.Response.StatusCode)
            );

            ResponseDurationMs.Record(
                elapsedMs,
                new("http.method", session.Request.Method),
                new("http.route", session.Request.RouteTemplate ?? "unmatched"),
                new("http.response.status_code", session.Response.StatusCode)
            );
        }

        #endregion meters

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

}