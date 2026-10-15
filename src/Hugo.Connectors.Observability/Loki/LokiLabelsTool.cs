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
    /// <c>loki_labels</c> lists stream label names, and <c>loki_label_values</c> lists the values of one label. Both
    /// are Read. Thread-safe.
    /// </summary>
    public class LokiLabelsTool : LokiToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema of <c>loki_labels</c>.
        /// </summary>
        public static readonly string LabelsInputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"start\":{\"type\":\"string\",\"description\":\"Window start. Default 6h before end.\"},"
            + "\"end\":{\"type\":\"string\",\"description\":\"Window end. Default now.\"}"
            + "}}";

        /// <summary>
        /// Input schema of <c>loki_label_values</c>.
        /// </summary>
        public static readonly string ValuesInputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"label\"],\"properties\":{"
            + "\"label\":{\"type\":\"string\",\"description\":\"Label name, for example app, namespace, or level.\"},"
            + "\"query\":{\"type\":\"string\",\"description\":\"Optional stream selector restricting the streams considered, for example {namespace=\\\"shop\\\"}.\"},"
            + "\"start\":{\"type\":\"string\",\"description\":\"Window start. Default 6h before end.\"},"
            + "\"end\":{\"type\":\"string\",\"description\":\"Window end. Default now.\"}"
            + "}}";

        #endregion

        #region Private-Members

        private static readonly Regex _LabelName = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
        private readonly bool _Values;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="values">True for <c>loki_label_values</c>, false for <c>loki_labels</c>.</param>
        public LokiLabelsTool(bool values) : base(values
            ? new ToolDescriptor
            {
                Name = "loki_label_values",
                Title = "Loki label values",
                Description = "List the values of one stream label (for example every app or namespace) in a window, optionally restricted by a stream selector. Use the values to build a precise selector.",
                InputSchemaJson = ValuesInputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "One value per line plus structured JSON {label, count, values[]}."
            }
            : new ToolDescriptor
            {
                Name = "loki_labels",
                Title = "Loki label names",
                Description = "List the stream label names seen in a window (for example app, namespace, level). Start here to learn how streams are labeled.",
                InputSchemaJson = LabelsInputSchema,
                BaseEffect = EffectEnum.Read,
                OutputDescription = "One name per line plus structured JSON {count, values[]}."
            })
        {
            _Values = values;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            string? label = null;
            string? selector = null;
            if (_Values)
            {
                label = Required(arguments, "label");
                if (!_LabelName.IsMatch(label)) throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'label' must be a label name such as app or namespace.");
                selector = arguments.GetString("query");
                if (!String.IsNullOrWhiteSpace(selector)) LokiQueryGuard.RequireStreamSelector(selector);
            }
            LokiSettings settings = ReadSettings(context);
            ObservabilityTimeWindow window = ObservabilityTime.ResolveWindow(arguments.GetString("start"), arguments.GetString("end"), "6h", settings.MaxRangeHours, DateTimeOffset.UtcNow);
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("start", ObservabilityTime.UnixNanoseconds(window.Start)),
                new KeyValuePair<string, string?>("end", ObservabilityTime.UnixNanoseconds(window.End)),
                new KeyValuePair<string, string?>("query", String.IsNullOrWhiteSpace(selector) ? null : selector)
            };
            string path = _Values ? "loki/api/v1/label/" + label + "/values" : "loki/api/v1/labels";
            PromResponse<List<string>> response = await Client(context.Resource, settings).GetAsync<List<string>>(path, q, token).ConfigureAwait(false);
            List<string> all = response.Data!.OrderBy(v => v, StringComparer.Ordinal).ToList();
            int cap = Math.Max(1, context.Limits.MaxRows);
            List<string> shown = all.Take(cap).ToList();
            bool more = all.Count > shown.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(shown.Count.ToString(CultureInfo.InvariantCulture)).Append(_Values ? " value(s) of " + label : " label name(s)");
            if (more) sb.Append(" (more exist)");
            sb.Append('\n');
            foreach (string v in shown) sb.Append(v).Append('\n');
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object?>
            {
                { "label", label },
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
