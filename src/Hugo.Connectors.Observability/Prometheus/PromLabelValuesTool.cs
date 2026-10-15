namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// <c>prom_label_values</c>: list the values of a label, or the label names when no label is given. Read.
    /// Thread-safe.
    /// </summary>
    public class PromLabelValuesTool : PromToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"label\":{\"type\":\"string\",\"description\":\"Label name, for example job or __name__ (metric names). Omit to list label names instead.\"},"
            + "\"match\":{\"type\":\"array\",\"maxItems\":20,\"items\":{\"type\":\"string\"},\"description\":\"Optional series selectors that restrict which series are considered.\"},"
            + "\"start\":{\"type\":\"string\",\"description\":\"Window start. Default 1h before end.\"},"
            + "\"end\":{\"type\":\"string\",\"description\":\"Window end. Default now.\"},"
            + "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000,\"description\":\"Most values to return. Capped by the resource's maxSeries.\"}"
            + "}}";

        #endregion

        #region Private-Members

        private static readonly Regex _LabelName = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromLabelValuesTool() : base(new ToolDescriptor
        {
            Name = "prom_label_values",
            Title = "Prometheus label values",
            Description = "List the values of one label (use __name__ for metric names), or omit label to list label names. Add match selectors to restrict to relevant series. Use it to discover jobs, instances, and metric names.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "One value per line plus structured JSON {label, count, more, values[]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            string? label = arguments.GetString("label");
            if (!String.IsNullOrWhiteSpace(label) && !_LabelName.IsMatch(label.Trim()))
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'label' must be a Prometheus label name such as job or __name__.");
            PrometheusSettings settings = ReadSettings(context);
            int cap = SeriesCap(arguments, settings, context.Limits);
            ObservabilityTimeWindow window = ObservabilityTime.ResolveWindow(arguments.GetString("start"), arguments.GetString("end"), "1h", settings.MaxRangeHours, DateTimeOffset.UtcNow);

            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>();
            foreach (string m in arguments.GetStringList("match").Where(m => !String.IsNullOrWhiteSpace(m)).Take(20)) q.Add(new KeyValuePair<string, string?>("match[]", m));
            q.Add(new KeyValuePair<string, string?>("start", ObservabilityTime.UnixSeconds(window.Start)));
            q.Add(new KeyValuePair<string, string?>("end", ObservabilityTime.UnixSeconds(window.End)));
            q.Add(new KeyValuePair<string, string?>("limit", (cap + 1).ToString(CultureInfo.InvariantCulture)));

            bool names = String.IsNullOrWhiteSpace(label);
            string path = names ? "api/v1/labels" : "api/v1/label/" + label!.Trim() + "/values";
            PromResponse<List<string>> response = await Client(context, settings).GetAsync<List<string>>(path, q, token).ConfigureAwait(false);
            List<string> all = response.Data!;
            List<string> shown = all.Take(cap).ToList();
            bool more = all.Count > shown.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(names ? " label name(s)" : " value(s) of " + label!.Trim());
            if (more) sb.Append(" (more exist; add a match selector)");
            sb.Append('\n');
            foreach (string v in shown) sb.Append(v).Append('\n');
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object?>
            {
                { "label", names ? null : label!.Trim() },
                { "count", shown.Count },
                { "more", more },
                { "values", shown }
            });
            ToolResult result = Finish(sb.ToString(), structured, context.Limits, more);
            result.Metrics["values"] = shown.Count;
            return result;
        }

        #endregion
    }
}
