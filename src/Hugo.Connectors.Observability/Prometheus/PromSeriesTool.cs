namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>prom_series</c>: list the label sets of series matching selectors in a window. Read. Thread-safe.
    /// </summary>
    public class PromSeriesTool : PromToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"match\"],\"properties\":{"
            + "\"match\":{\"type\":\"array\",\"minItems\":1,\"maxItems\":20,\"items\":{\"type\":\"string\"},\"description\":\"Series selectors such as up{job=\\\"api\\\"} or {__name__=~\\\"http_.*\\\"}. At least one.\"},"
            + "\"start\":{\"type\":\"string\",\"description\":\"Window start: RFC 3339, Unix seconds, or a duration before now. Default 1h before end.\"},"
            + "\"end\":{\"type\":\"string\",\"description\":\"Window end. Default now.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most series to return. Capped by the resource's maxSeries.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromSeriesTool() : base(new ToolDescriptor
        {
            Name = "prom_series",
            Title = "Find Prometheus series",
            Description = "List the label sets of series that match one or more selectors in a window, without sample values. Use it to discover which labels and values exist before writing a query.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "One series per line plus structured JSON {count, omitted, series[{labels}]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            List<string> match = arguments.GetStringList("match").Where(m => !String.IsNullOrWhiteSpace(m)).ToList();
            if (match.Count == 0) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'match' needs at least one series selector, for example up or {job=\"api\"}.");
            if (match.Count > 20) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'match' accepts at most 20 selectors.");
            PrometheusSettings settings = ReadSettings(context);
            int cap = SeriesCap(arguments, settings, context.Limits);
            ObservabilityTimeWindow window = ObservabilityTime.ResolveWindow(arguments.GetString("start"), arguments.GetString("end"), "1h", settings.MaxRangeHours, DateTimeOffset.UtcNow);

            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>();
            foreach (string m in match) q.Add(new KeyValuePair<string, string?>("match[]", m));
            q.Add(new KeyValuePair<string, string?>("start", ObservabilityTime.UnixSeconds(window.Start)));
            q.Add(new KeyValuePair<string, string?>("end", ObservabilityTime.UnixSeconds(window.End)));
            q.Add(new KeyValuePair<string, string?>("limit", (cap + 1).ToString(CultureInfo.InvariantCulture)));
            PromResponse<List<Dictionary<string, string>>> response = await Client(context, settings).GetAsync<List<Dictionary<string, string>>>("api/v1/series", q, token).ConfigureAwait(false);

            List<Dictionary<string, string>> all = response.Data!;
            List<Dictionary<string, string>> shown = all.Take(cap).ToList();
            bool more = all.Count > shown.Count || (response.Warnings != null && response.Warnings.Any(w => w.Contains("limit", StringComparison.OrdinalIgnoreCase)));
            StringBuilder sb = new StringBuilder();
            sb.Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(" series");
            if (more) sb.Append(" (more exist; narrow the selector or raise limit up to maxSeries)");
            sb.Append('\n');
            foreach (Dictionary<string, string> s in shown) sb.Append(PromResultRenderer.LabelSet(s)).Append('\n');
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object>
            {
                { "count", shown.Count },
                { "more", more },
                { "series", shown }
            });
            ToolResult result = Finish(sb.ToString(), structured, context.Limits, more);
            result.Metrics["series"] = shown.Count;
            return result;
        }

        #endregion
    }
}
