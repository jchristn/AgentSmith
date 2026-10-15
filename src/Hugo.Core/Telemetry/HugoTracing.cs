namespace Hugo.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Span and timing helpers shared by the server and worker (P13.24, P13.25): background service iterations,
    /// outbound calls, and generic operation spans on the <c>Hugo</c> activity source, each with its matching metrics.
    /// Spans carry OpenTelemetry attribute names where one exists and <c>hugo.*</c> otherwise, never secret values.
    /// Everything here is best-effort and never throws on its own account. Thread-safe.
    /// </summary>
    public static class HugoTracing
    {
        #region Public-Members

        /// <summary>
        /// Activity source for Hugo spans (name Hugo).
        /// </summary>
        public static ActivitySource Source
        {
            get
            {
                return _Source;
            }
        }

        /// <summary>
        /// Span attribute set to true on background iterations that found nothing to do. The collector drops traces
        /// that carry it, so idle polling does not flood the trace store.
        /// </summary>
        public static readonly string IdleAttribute = "hugo.idle";

        #endregion

        #region Private-Members

        private static readonly ActivitySource _Source = new ActivitySource("Hugo");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a span on the Hugo source.
        /// </summary>
        /// <param name="name">Span name, for example "mcp hugo_get_run".</param>
        /// <param name="kind">Span kind.</param>
        /// <param name="requireParent">When true, no span is started unless there is a current span to nest under.</param>
        /// <returns>Activity, or null when nothing samples it.</returns>
        public static Activity? Start(string name, ActivityKind kind = ActivityKind.Internal, bool requireParent = false)
        {
            if (String.IsNullOrEmpty(name)) return null;
            if (requireParent && Activity.Current == null) return null;
            try
            {
                return Source.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Mark a span failed with the exception type and message (no stack, no values).
        /// </summary>
        /// <param name="activity">Span, or null.</param>
        /// <param name="exception">Exception.</param>
        public static void Fail(Activity? activity, Exception exception)
        {
            if (activity == null || exception == null) return;
            try
            {
                activity.SetStatus(ActivityStatusCode.Error, exception.GetType().Name + ": " + exception.Message);
                activity.SetTag("error.type", exception.GetType().Name);
                ActivityTagsCollection tags = new ActivityTagsCollection();
                tags.Add("exception.type", exception.GetType().FullName);
                tags.Add("exception.message", exception.Message);
                activity.AddEvent(new ActivityEvent("exception", DateTimeOffset.UtcNow, tags));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Mark a span failed with a description.
        /// </summary>
        /// <param name="activity">Span, or null.</param>
        /// <param name="description">Description.</param>
        public static void Fail(Activity? activity, string description)
        {
            if (activity == null) return;
            try
            {
                activity.SetStatus(ActivityStatusCode.Error, description);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Mark a span successful.
        /// </summary>
        /// <param name="activity">Span, or null.</param>
        public static void Ok(Activity? activity)
        {
            if (activity == null) return;
            try
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Run one iteration of a background service inside a span named "&lt;service&gt; &lt;operation&gt;", recording
        /// hugo.background.duration and hugo.background.iterations with result ok, idle, or error. The work
        /// returns whether it did anything; idle iterations are tagged so the collector can drop their traces.
        /// Exceptions propagate after they are recorded; cancellation is recorded as neither error nor ok.
        /// </summary>
        /// <param name="service">Service, for example maintenance.</param>
        /// <param name="operation">Operation, for example reap_leases.</param>
        /// <param name="work">Work; returns true when it did something.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What the work returned.</returns>
        /// <exception cref="ArgumentNullException">Thrown when work is null.</exception>
        public static async Task<bool> BackgroundAsync(string service, string operation, Func<CancellationToken, Task<bool>> work, CancellationToken token)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            long start = Stopwatch.GetTimestamp();
            Activity? previous = Activity.Current;
            // Background iterations are root spans: they must not join whatever span happened to be current.
            Activity.Current = null;
            Activity? activity = Start(service + " " + operation);
            if (activity != null)
            {
                activity.SetTag("hugo.service", service);
                activity.SetTag("hugo.operation", operation);
            }
            string result = "ok";
            try
            {
                bool didWork = await work(token).ConfigureAwait(false);
                if (!didWork)
                {
                    result = "idle";
                    activity?.SetTag(IdleAttribute, "true");
                }
                Ok(activity);
                return didWork;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                result = "cancelled";
                throw;
            }
            catch (Exception e)
            {
                result = "error";
                Fail(activity, e);
                throw;
            }
            finally
            {
                activity?.Dispose();
                Activity.Current = previous;
                if (result != "cancelled")
                {
                    TagList tags = HugoMetrics.Tags("service", service, "operation", operation, "result", result);
                    HugoMetrics.Observe(HugoMetricCatalog.BackgroundDuration, HugoMetrics.SecondsSince(start), tags);
                    HugoMetrics.Count(HugoMetricCatalog.BackgroundIterations, 1, tags);
                }
            }
        }

        /// <summary>
        /// Run one iteration of a background service that always counts as having done work.
        /// </summary>
        /// <param name="service">Service.</param>
        /// <param name="operation">Operation.</param>
        /// <param name="work">Work.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when work is null.</exception>
        public static async Task BackgroundAsync(string service, string operation, Func<CancellationToken, Task> work, CancellationToken token)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            await BackgroundAsync(service, operation, async ct =>
            {
                await work(ct).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Start a client span for an outbound call, named "&lt;service&gt; &lt;operation&gt;". Pair it with
        /// <see cref="EndOutbound"/>.
        /// </summary>
        /// <param name="service">Downstream service, for example mcp_server, grafana, kubernetes.</param>
        /// <param name="operation">Operation, for example test, probe, create_job.</param>
        /// <returns>Span, or null.</returns>
        public static Activity? StartOutbound(string service, string operation)
        {
            Activity? activity = Start(service + " " + operation, ActivityKind.Client);
            if (activity != null)
            {
                activity.SetTag("peer.service", service);
                activity.SetTag("hugo.operation", operation);
            }
            return activity;
        }

        /// <summary>
        /// End an outbound call: record hugo.outbound.requests and hugo.outbound.duration and close the span.
        /// </summary>
        /// <param name="activity">Span from <see cref="StartOutbound"/>, or null.</param>
        /// <param name="service">Downstream service.</param>
        /// <param name="operation">Operation.</param>
        /// <param name="startTimestamp">Stopwatch timestamp taken before the call.</param>
        /// <param name="result">Result: ok, error, timeout, or an HTTP status class.</param>
        /// <param name="exception">Exception that ended the call, or null.</param>
        public static void EndOutbound(Activity? activity, string service, string operation, long startTimestamp, string result, Exception? exception = null)
        {
            try
            {
                if (exception != null) Fail(activity, exception);
                else if (result == "ok" || result == "2xx" || result == "3xx") Ok(activity);
                else Fail(activity, result);
                activity?.SetTag("hugo.result", result);
                activity?.Dispose();
                TagList tags = HugoMetrics.Tags("service", service, "operation", operation, "result", result);
                HugoMetrics.Observe(HugoMetricCatalog.OutboundDuration, HugoMetrics.SecondsSince(startTimestamp), tags);
                HugoMetrics.Count(HugoMetricCatalog.OutboundRequests, 1, tags);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Log fields for a line: key=value pairs for every non-empty value, in order, separated by spaces, with no
        /// leading or trailing space. Values containing spaces are quoted.
        /// </summary>
        /// <param name="pairs">Alternating keys and values.</param>
        /// <returns>Fields.</returns>
        public static string LogFields(params string?[] pairs)
        {
            if (pairs == null || pairs.Length < 2) return String.Empty;
            List<string> parts = new List<string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                string? key = pairs[i];
                string? value = pairs[i + 1];
                if (String.IsNullOrEmpty(key) || String.IsNullOrEmpty(value)) continue;
                if (value.IndexOf(' ') >= 0 || value.IndexOf('"') >= 0) value = "\"" + value.Replace("\"", "'") + "\"";
                parts.Add(key + "=" + value);
            }
            return String.Join(" ", parts);
        }

        #endregion
    }
}
