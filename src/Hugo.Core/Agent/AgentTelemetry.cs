namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Agent and model metrics and spans (plan section 21) on the <c>Hugo</c> meter and activity source. When the
    /// shared <c>HugoMetrics</c>/<c>HugoTracing</c> classes land in <c>Hugo.Core/Telemetry</c>, these instruments can
    /// move there unchanged; listeners subscribe by name, so a second <see cref="Meter"/> named <c>Hugo</c> is
    /// harmless. Label values are normalized to short lowercase tokens to keep cardinality bounded. Every method
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
        private static readonly Meter _Meter = new Meter("Hugo");
        private static readonly Counter<long> _Turns = _Meter.CreateCounter<long>("hugo_agent_turns_total", null, "Agent turns by surface and outcome.");
        private static readonly Histogram<double> _TurnDuration = _Meter.CreateHistogram<double>("hugo_agent_turn_duration_seconds", "s", "Agent turn duration.");
        private static readonly Histogram<double> _StageDuration = _Meter.CreateHistogram<double>("hugo_agent_stage_duration_seconds", "s", "Agent turn stage duration.");
        private static readonly Histogram<long> _Iterations = _Meter.CreateHistogram<long>("hugo_agent_iterations", null, "Model iterations per agent turn.");
        private static readonly Counter<long> _ModelRequests = _Meter.CreateCounter<long>("hugo_model_requests_total", null, "Model requests by provider, kind, and outcome.");
        private static readonly Histogram<double> _ModelDuration = _Meter.CreateHistogram<double>("hugo_model_request_duration_seconds", "s", "Model request duration.");
        private static readonly Histogram<double> _ModelTtft = _Meter.CreateHistogram<double>("hugo_model_time_to_first_token_seconds", "s", "Model time to first token.");
        private static readonly Counter<long> _ModelTokens = _Meter.CreateCounter<long>("hugo_model_tokens_total", null, "Model tokens by provider and direction.");

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
                _Turns.Add(1, new KeyValuePair<string, object?>("surface", s), new KeyValuePair<string, object?>("outcome", Label(outcome)));
                _TurnDuration.Record(seconds, new KeyValuePair<string, object?>("surface", s));
                _Iterations.Record(iterations);
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
                _StageDuration.Record(seconds, new KeyValuePair<string, object?>("stage", Label(stage)));
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
                _ModelRequests.Add(1, new KeyValuePair<string, object?>("provider", p), new KeyValuePair<string, object?>("kind", k), new KeyValuePair<string, object?>("outcome", Label(outcome)));
                _ModelDuration.Record(seconds, new KeyValuePair<string, object?>("provider", p), new KeyValuePair<string, object?>("kind", k));
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
                _ModelTtft.Record(seconds, new KeyValuePair<string, object?>("provider", Label(provider)));
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
                if (inputTokens > 0) _ModelTokens.Add(inputTokens, new KeyValuePair<string, object?>("provider", p), new KeyValuePair<string, object?>("direction", "input"));
                if (outputTokens > 0) _ModelTokens.Add(outputTokens, new KeyValuePair<string, object?>("provider", p), new KeyValuePair<string, object?>("direction", "output"));
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
