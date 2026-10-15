namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>loki_query</c>: evaluate a LogQL query at one instant (normally a metric query). Read. Thread-safe.
    /// </summary>
    public class LokiQueryTool : LokiToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"query\"],\"properties\":{"
            + "\"query\":{\"type\":\"string\",\"description\":\"LogQL query with a stream selector, normally a metric query such as sum by (level) (count_over_time({app=\\\"api\\\"}[5m])).\"},"
            + "\"time\":{\"type\":\"string\",\"description\":\"Evaluation time: RFC 3339, Unix seconds, 'now', or a duration before now. Default now.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":5000,\"description\":\"Most lines or series to return. Capped by the resource's maxLines. Default 100.\"},"
            + "\"direction\":{\"type\":\"string\",\"enum\":[\"backward\",\"forward\"],\"description\":\"backward returns newest lines first, forward oldest first. Default backward.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public LokiQueryTool() : base(new ToolDescriptor
        {
            Name = "loki_query",
            Title = "LogQL instant query",
            Description = "Evaluate a LogQL query at one instant. Use it for metric queries over a recent window, such as error counts per service over the last 5 minutes. "
                + "The query must contain a stream selector like {app=\"api\"}. For log lines over a period use loki_query_range.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Series table (metric queries) or compact lines (log queries) plus structured JSON."
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
            string? time = arguments.GetString("time");
            DateTimeOffset at = String.IsNullOrWhiteSpace(time) ? DateTimeOffset.UtcNow : ObservabilityTime.ParsePoint(time, DateTimeOffset.UtcNow, "time");
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("query", query),
                new KeyValuePair<string, string?>("time", ObservabilityTime.UnixNanoseconds(at)),
                new KeyValuePair<string, string?>("limit", cap.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string?>("direction", backward ? "backward" : "forward")
            };
            return RunQueryAsync(context, settings, "loki/api/v1/query", q, query, backward, cap, null, token);
        }

        #endregion
    }
}
