namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Hugo.Core.Telemetry;

    /// <summary>
    /// Agent and model metrics and spans (plan section 21). Metrics are recorded through <see cref="HugoMetrics"/> with
    /// the agent and model descriptors of <see cref="HugoMetricCatalog"/>; spans use the <c>Hugo</c> activity source.
    /// Label values are normalized to short lowercase tokens to keep cardinality bounded. Every method
    /// swallows its own failures: telemetry never breaks a request (plan 6.4). Thread-safe.
    /// </summary>
    public static class AgentTelemetry
    {
        #region Public-Members

        /// <summary>
        /// Meter and activity source name.
        /// </summary>
        public static string SourceName
        {
            get
            {
                return "Hugo";
            }
        }

        /// <summary>
        /// Activity source for agent and model spans.
        /// </summary>
        public static ActivitySource ActivitySource
        {
            get
            {
                return _ActivitySource;
            }
        }

        /// <summary>
        /// Stage names used with <see cref="RecordStage"/>.
        /// </summary>
        public static IReadOnlyList<string> Stages
        {
            get
            {
                return _Stages;
            }
        }

        #endregion

        #region Private-Members

        private static readonly string[] _Stages = new string[] { "context_build", "compaction", "tool_retrieval", "inference", "tool", "approval_wait", "persist" };
        private static readonly ActivitySource _ActivitySource = new ActivitySource("Hugo");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start a span, or return null when nobody listens or starting fails.
        /// </summary>
        /// <param name="name">Span name (for example <c>agent.turn</c>, <c>stage:inference</c>).</param>
        /// <param name="kind">Span kind.</param>
        /// <returns>Activity or null.</returns>
        public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
        {
            try
            {
                return _ActivitySource.StartActivity(name, kind);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Set a tag on a span without throwing.
        /// </summary>
        /// <param name="activity">Span or null.</param>
        /// <param name="key">Tag key.</param>
        /// <param name="value">Tag value.</param>
        public static void SetTag(Activity? activity, string key, object? value)
        {
            if (activity == null) return;
            try
            {
                activity.SetTag(key, value);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Mark a span failed without throwing.
        /// </summary>
        /// <param name="activity">Span or null.</param>
        /// <param name="description">Short failure description (no secrets).</param>
        public static void SetError(Activity? activity, string description)
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
        /// Stop a span without throwing.
        /// </summary>
        /// <param name="activity">Span or null.</param>
        public static void Stop(Activity? activity)
        {
            if (activity == null) return;
            try
            {
                activity.Dispose();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a finished turn.
        /// </summary>
        /// <param name="surface">Surface (<c>chat</c>, <c>a2a</c>, <c>openai</c>, <c>mcp</c>).</param>
        /// <param name="outcome">Outcome (finish reason).</param>
        /// <param name="seconds">Duration.</param>
        /// <param name="iterations">Model iterations.</param>
        public static void RecordTurn(string surface, string outcome, double seconds, int iterations)
        {
            try
            {
                string s = Label(surface);
                HugoMetrics.Count(HugoMetricCatalog.AgentTurns, "surface", s, "outcome", Label(outcome));
                HugoMetrics.Observe(HugoMetricCatalog.AgentTurnDuration, seconds, "surface", s);
                HugoMetrics.Observe(HugoMetricCatalog.AgentIterations, iterations);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a stage duration.
        /// </summary>
        /// <param name="stage">One of <see cref="Stages"/>.</param>
        /// <param name="seconds">Duration.</param>
        public static void RecordStage(string stage, double seconds)
        {
            try
            {
                HugoMetrics.Observe(HugoMetricCatalog.AgentStageDuration, seconds, "stage", Label(stage));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record one model request.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="kind"><c>chat</c> or <c>embedding</c>.</param>
        /// <param name="outcome"><c>success</c> or a failure code.</param>
        /// <param name="seconds">Duration.</param>
        public static void RecordModelRequest(string provider, string kind, string outcome, double seconds)
        {
            try
            {
                string p = Label(provider);
                string k = Label(kind);
                HugoMetrics.Count(HugoMetricCatalog.ModelRequests, "provider", p, "kind", k, "outcome", Label(outcome));
                HugoMetrics.Observe(HugoMetricCatalog.ModelRequestDuration, seconds, "provider", p, "kind", k);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record time to first token.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="seconds">Seconds.</param>
        public static void RecordTimeToFirstToken(string provider, double seconds)
        {
            try
            {
                HugoMetrics.Observe(HugoMetricCatalog.ModelTimeToFirstToken, seconds, "provider", Label(provider));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record token counts.
        /// </summary>
        /// <param name="provider">Provider.</param>
        /// <param name="inputTokens">Input tokens.</param>
        /// <param name="outputTokens">Output tokens.</param>
        public static void RecordTokens(string provider, long inputTokens, long outputTokens)
        {
            try
            {
                string p = Label(provider);
                if (inputTokens > 0) HugoMetrics.Count(HugoMetricCatalog.ModelTokens, inputTokens, HugoMetrics.Tags("provider", p, "direction", "input"));
                if (outputTokens > 0) HugoMetrics.Count(HugoMetricCatalog.ModelTokens, outputTokens, HugoMetrics.Tags("provider", p, "direction", "output"));
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Private-Methods

        private static string Label(string? value)
        {
            if (String.IsNullOrEmpty(value)) return "unknown";
            if (value.Length > 40) return "other";
            char[] chars = value.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.';
                if (!ok) return "other";
            }
            return new string(chars);
        }

        #endregion
    }
}
