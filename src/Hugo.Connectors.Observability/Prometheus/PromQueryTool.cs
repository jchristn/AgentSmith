namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>prom_query</c>: evaluate a PromQL expression at one instant. Read. Thread-safe.
    /// </summary>
    public class PromQueryTool : PromToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"query\"],\"properties\":{"
            + "\"query\":{\"type\":\"string\",\"description\":\"PromQL expression. Prefer aggregations such as sum by (job) (rate(http_requests_total[5m])) over raw series.\"},"
            + "\"time\":{\"type\":\"string\",\"description\":\"Evaluation time: RFC 3339, Unix seconds, 'now', or a duration before now such as 15m. Default now.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most series to return. Capped by the resource's maxSeries.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromQueryTool() : base(new ToolDescriptor
        {
            Name = "prom_query",
            Title = "PromQL instant query",
            Description = "Evaluate a PromQL expression at one instant and return each series with its value. Use it for current state (up, error rates, saturation). "
                + "Aggregate in PromQL (sum, rate, topk) to keep the result small; results beyond maxSeries are dropped and reported.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table of series, value, time plus structured JSON {resultType, query, omittedSeries, series[{labels, time, value}]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            string query = Required(arguments, "query");
            PrometheusSettings settings = ReadSettings(context);
            int cap = SeriesCap(arguments, settings, context.Limits);
            string? time = arguments.GetString("time");
            DateTimeOffset at = String.IsNullOrWhiteSpace(time) ? DateTimeOffset.UtcNow : ObservabilityTime.ParsePoint(time, DateTimeOffset.UtcNow, "time");

            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("query", query),
                new KeyValuePair<string, string?>("time", ObservabilityTime.UnixSeconds(at))
            };
            PromResponse<PromQueryData> response = await Client(context, settings).GetAsync<PromQueryData>("api/v1/query", q, token).ConfigureAwait(false);
            ObservabilityRendering r = PromResultRenderer.Render(response.Data!, ProviderName, query, cap, null, 0, Notes(response.Warnings));
            ToolResult result = Finish(r.Text, r.StructuredJson, context.Limits, r.Truncated);
            result.Metrics["series"] = r.Returned;
            result.Metrics["omittedSeries"] = r.Omitted;
            return result;
        }

        #endregion
    }
}
