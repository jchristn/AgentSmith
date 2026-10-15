namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using Hugo.Core.Security;
    using Hugo.Core.Telemetry;
    using Hugo.Server.Routes;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.Routing;

    /// <summary>
    /// Per-request REST telemetry (P13.22), recorded once per request from the server's post-routing hook so every
    /// route is covered without per-route code: hugo.http.requests, hugo.http.request.duration, and the body
    /// size histograms, labeled with the route template Watson matched (never the raw path), method, status, and
    /// tenant. Requests the client abandoned are recorded with status 499 and status class aborted, so they never count as
    /// server errors. Requests that matched no route are labeled unmatched (or unauthenticated when authentication rejected
    /// them first). Watson's own server span covers the trace side. Thread-safe.
    /// </summary>
    public static class RequestTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Route label for requests that matched no route.
        /// </summary>
        public static readonly string Unmatched = "unmatched";

        /// <summary>
        /// Route label for requests rejected by authentication before routing.
        /// </summary>
        public static readonly string Unauthenticated = "unauthenticated";

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<Guid, Activity> _Spans = new ConcurrentDictionary<Guid, Activity>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The route template a request matched.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Route template, <see cref="Unauthenticated"/>, or <see cref="Unmatched"/>.</returns>
        public static string RouteTemplate(HttpContextBase context)
        {
            if (context == null) return Unmatched;
            string? path = null;
            if (context.Route is ParameterRoute parameter) path = parameter.Path;
            else if (context.Route is StaticRoute stat) path = stat.Path;
            if (!String.IsNullOrEmpty(path))
            {
                // Watson stores static routes with a trailing slash; report the template as registered.
                if (path.Length > 1 && path.EndsWith("/", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 1);
                return path;
            }
            if (context.Response != null && context.Response.StatusCode == 401) return Unauthenticated;
            return Unmatched;
        }

        /// <summary>
        /// The tenant a request concerns: the {tenantId} path parameter when present, else the caller's tenant.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Tenant identifier, or null.</returns>
        public static string? Tenant(HttpContextBase context)
        {
            if (context == null) return null;
            string? fromPath = null;
            try
            {
                fromPath = RouteHelpers.Param(context, "tenantId");
            }
            catch (Exception)
            {
            }
            if (!String.IsNullOrEmpty(fromPath)) return fromPath;
            if (context.Metadata is RequestContext requestContext) return requestContext.TenantId;
            return null;
        }

        /// <summary>
        /// Remember the request's server span while it is current (authentication and route handlers run inside it;
        /// post-routing runs after Watson has ended it), so the request metrics recorded in post-routing carry it as
        /// their exemplar. The first capture wins.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        public static void CaptureSpan(HttpContextBase context)
        {
            if (context?.Request == null) return;
            Activity? current = Activity.Current;
            if (current != null) _Spans.TryAdd(context.Request.Guid, current);
        }

        /// <summary>
        /// W3C trace identifier of the request's span, without removing it, or null.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Trace identifier (32 hex characters), or null.</returns>
        public static string? TraceId(HttpContextBase context)
        {
            if (context?.Request == null) return null;
            if (_Spans.TryGetValue(context.Request.Guid, out Activity? span)) return span.TraceId.ToHexString();
            return Activity.Current?.TraceId.ToHexString();
        }

        /// <summary>
        /// Take the span remembered for a request, removing it.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <returns>Span, or null.</returns>
        public static Activity? TakeSpan(HttpContextBase context)
        {
            if (context?.Request == null) return null;
            return _Spans.TryRemove(context.Request.Guid, out Activity? span) ? span : null;
        }

        /// <summary>
        /// Record a finished request.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="route">Route template from <see cref="RouteTemplate"/>.</param>
        /// <param name="tenant">Tenant from <see cref="Tenant"/>, or null.</param>
        /// <param name="seconds">Request duration in seconds.</param>
        public static void Record(HttpContextBase context, string route, string? tenant, double seconds)
        {
            if (context == null) return;
            try
            {
                string method = context.Request.Method.ToString();
                int status = context.Response.StatusCode;
                string statusClass = HugoMetrics.StatusClass(status);
                // A client that hangs up before the answer (a worker abandoning its cancellation watch whenever its run
                // set changes, a browser leaving a page) is not a server error: report it as 499, class aborted.
                if (context.RequestAborted)
                {
                    status = 499;
                    statusClass = "aborted";
                }
                if (HugoMetrics.Enabled(HugoMetricCatalog.HttpRequests))
                {
                    TagList counted = HugoMetrics.Tags("method", method, "route", route, "status_code", status.ToString(), "status_class", statusClass);
                    counted.Add("tenant", HugoMetrics.Fold("tenant", tenant));
                    HugoMetrics.Count(HugoMetricCatalog.HttpRequests, 1, counted);
                }
                if (HugoMetrics.Enabled(HugoMetricCatalog.HttpRequestDuration))
                {
                    TagList timed = HugoMetrics.Tags("method", method, "route", route, "status_class", statusClass, "tenant", tenant);
                    HugoMetrics.Observe(HugoMetricCatalog.HttpRequestDuration, seconds, timed);
                }
                if (HugoMetrics.Enabled(HugoMetricCatalog.HttpRequestSize))
                {
                    TagList sized = HugoMetrics.Tags("method", method, "route", route);
                    HugoMetrics.Observe(HugoMetricCatalog.HttpRequestSize, Math.Max(0, context.Request.ContentLength), sized);
                    HugoMetrics.Observe(HugoMetricCatalog.HttpResponseSize, Math.Max(0, context.Response.ContentLength), sized);
                }
                Activity? current = Activity.Current;
                if (current != null && !String.IsNullOrEmpty(tenant)) current.SetTag("hugo.tenant", tenant);
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}
