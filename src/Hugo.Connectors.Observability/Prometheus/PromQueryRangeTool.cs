namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>prom_query_range</c>: evaluate a PromQL expression over a bounded window at a step chosen to respect the
    /// resource's point budget. Read. Thread-safe.
    /// </summary>
    public class PromQueryRangeTool : PromToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"query\"],\"properties\":{"
            + "\"query\":{\"type\":\"string\",\"description\":\"PromQL expression. Use rate() for counters and aggregate (sum by, topk) to limit series.\"},"
            + "\"start\":{\"type\":\"string\",\"description\":\"Window start: RFC 3339, Unix seconds, or a duration before now such as 1h. Default 1h before end.\"},"
            + "\"end\":{\"type\":\"string\",\"description\":\"Window end: RFC 3339, Unix seconds, 'now', or a duration before now. Default now.\"},"
            + "\"step\":{\"type\":\"string\",\"description\":\"Resolution such as 30s, 1m, 5m. Omit to pick one automatically. A step that would exceed the resource's maxPointsPerSeries is raised.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most series to return. Capped by the resource's maxSeries.\"}"
            + "}}";

        /// <summary>
        /// Points aimed for per series when no step is given. Minimum 2, maximum 11000, default 60.
        /// </summary>
        public int DefaultPoints
        {
            get
            {
                return _DefaultPoints;
            }
            set
            {
                _DefaultPoints = Math.Clamp(value, 2, 11000);
            }
        }

        #endregion

        #region Private-Members

        private int _DefaultPoints = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromQueryRangeTool() : base(new ToolDescriptor
        {
            Name = "prom_query_range",
            Title = "PromQL range query",
            Description = "Evaluate a PromQL expression over a time window and summarize each series (points, min, max, avg, last) with its values. "
                + "Always bound the window (default 1h; longer than the resource's maxRangeHours fails with time_range_exceeded). "
                + "The step is raised automatically to keep every series within maxPointsPerSeries. Prefer rate() and aggregations over raw series.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Summary table per series and time=value lists, plus structured JSON {resultType, query, start, end, stepSeconds, omittedSeries, series[{labels, values[[unixSeconds, value]]}]}."
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
            ObservabilityTimeWindow window = ObservabilityTime.ResolveWindow(arguments.GetString("start"), arguments.GetString("end"), "1h", settings.MaxRangeHours, DateTimeOffset.UtcNow);
            string? stepText = arguments.GetString("step");
            TimeSpan? requested = String.IsNullOrWhiteSpace(stepText) ? null : ObservabilityTime.ParseDuration(stepText, "step");
            PromStepPlan plan = PromStepPlan.Compute(window.Length, requested, settings.MaxPointsPerSeries, _DefaultPoints);

            List<string> notes = new List<string>();
            if (plan.Raised)
                notes.Add("step raised from " + stepText + " to " + ObservabilityTime.FormatDuration(TimeSpan.FromSeconds(plan.StepSeconds)) + " to stay within maxPointsPerSeries (" + settings.MaxPointsPerSeries.ToString(CultureInfo.InvariantCulture) + ").");

            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("query", query),
                new KeyValuePair<string, string?>("start", ObservabilityTime.UnixSeconds(window.Start)),
                new KeyValuePair<string, string?>("end", ObservabilityTime.UnixSeconds(window.End)),
                new KeyValuePair<string, string?>("step", plan.StepSeconds.ToString(CultureInfo.InvariantCulture))
            };
            PromResponse<PromQueryData> response = await Client(context, settings).GetAsync<PromQueryData>("api/v1/query_range", q, token).ConfigureAwait(false);
            notes.AddRange(Notes(response.Warnings));
            ObservabilityRendering r = PromResultRenderer.Render(response.Data!, ProviderName, query, cap, window, plan.StepSeconds, notes);
            ToolResult result = Finish(r.Text, r.StructuredJson, context.Limits, r.Truncated);
            result.Metrics["series"] = r.Returned;
            result.Metrics["omittedSeries"] = r.Omitted;
            result.Metrics["stepSeconds"] = plan.StepSeconds;
            result.Metrics["stepRaised"] = plan.Raised ? 1 : 0;
            return result;
        }

        #endregion
    }
}
