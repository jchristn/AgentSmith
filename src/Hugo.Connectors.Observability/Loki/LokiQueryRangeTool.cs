namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>loki_query_range</c>: run a LogQL query over a bounded window, returning log lines or metric series. Read.
    /// Thread-safe.
    /// </summary>
    public class LokiQueryRangeTool : LokiToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"query\"],\"properties\":{"
            + "\"query\":{\"type\":\"string\",\"description\":\"LogQL query with a stream selector, for example {app=\\\"api\\\"} |= \\\"error\\\" or rate({app=\\\"api\\\"}[5m]).\"},"
            + "\"start\":{\"type\":\"string\",\"description\":\"Window start: RFC 3339, Unix seconds, or a duration before now such as 15m. Default 1h before end.\"},"
            + "\"end\":{\"type\":\"string\",\"description\":\"Window end: RFC 3339, Unix seconds, 'now', or a duration before now. Default now.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5000,\"description\":\"Most lines to return. Capped by the resource's maxLines. Default 100.\"},"
            + "\"direction\":{\"type\":\"string\",\"enum\":[\"backward\",\"forward\"],\"description\":\"backward returns newest lines first, forward oldest first. Default backward.\"},"
            + "\"step\":{\"type\":\"string\",\"description\":\"Resolution for metric queries, such as 1m. Ignored for log queries. Default chosen by Loki.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public LokiQueryRangeTool() : base(new ToolDescriptor
        {
            Name = "loki_query_range",
            Title = "LogQL range query",
            Description = "Run a LogQL query over a bounded time window. Log queries return compact lines (time, stream key, line) with a legend of stream labels; "
                + "metric queries return per-series summaries. Always include a stream selector, filter with |= or |~ to keep results small, and bound the window "
                + "(windows longer than the resource's maxRangeHours fail with time_range_exceeded).",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Lines 'time sN line' with a stream legend, or a series summary, plus structured JSON {resultType, query, start, end, count, omitted, streams, lines[]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            string query = Required(arguments, "query");
            LokiQueryGuard.RequireStreamSelector(query);
            LokiSettings settings = ReadSettings(context);
            int cap = LineCap(arguments, settings, context.Limits);
            bool backward = Backward(arguments);
            ObservabilityTimeWindow window = ObservabilityTime.ResolveWindow(arguments.GetString("start"), arguments.GetString("end"), "1h", settings.MaxRangeHours, DateTimeOffset.UtcNow);
            string? stepText = arguments.GetString("step");
            string? step = null;
            if (!String.IsNullOrWhiteSpace(stepText))
            {
                TimeSpan parsed = ObservabilityTime.ParseDuration(stepText, "step");
                PromStepPlan plan = PromStepPlan.Compute(window.Length, parsed, 11000, 60);
                step = plan.StepSeconds.ToString(CultureInfo.InvariantCulture) + "s";
            }
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("query", query),
                new KeyValuePair<string, string?>("start", ObservabilityTime.UnixNanoseconds(window.Start)),
                new KeyValuePair<string, string?>("end", ObservabilityTime.UnixNanoseconds(window.End)),
                new KeyValuePair<string, string?>("limit", cap.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string?>("direction", backward ? "backward" : "forward"),
                new KeyValuePair<string, string?>("step", step)
            };
            return RunQueryAsync(context, settings, "loki/api/v1/query_range", q, query, backward, cap, window, token);
        }

        #endregion
    }
}
