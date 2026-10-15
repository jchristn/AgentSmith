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
    /// <c>prom_alerts</c>: list active (pending and firing) alerts. Read. Thread-safe.
    /// </summary>
    public class PromAlertsTool : PromToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"state\":{\"type\":\"string\",\"enum\":[\"any\",\"firing\",\"pending\"],\"description\":\"Only alerts in this state. Default any.\"},"
            + "\"name\":{\"type\":\"string\",\"description\":\"Only alerts whose alertname contains this text (case-insensitive).\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromAlertsTool() : base(new ToolDescriptor
        {
            Name = "prom_alerts",
            Title = "Active Prometheus alerts",
            Description = "List alerts Prometheus is currently evaluating as pending or firing, with labels, annotations, value, and when each became active. Start incident triage here.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table of alertname, state, activeAt, value, labels, summary plus structured JSON {count, omitted, alerts[]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            PrometheusSettings settings = ReadSettings(context);
            string state = (arguments.GetString("state", "any") ?? "any").Trim().ToLowerInvariant();
            if (state != "any" && state != "firing" && state != "pending")
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'state' must be any, firing, or pending.");
            string? name = arguments.GetString("name");

            PromResponse<PromAlertsData> response = await Client(context, settings).GetAsync<PromAlertsData>("api/v1/alerts", null, token).ConfigureAwait(false);
            List<PromAlert> alerts = response.Data!.Alerts
                .Where(a => state == "any" || String.Equals(a.State, state, StringComparison.OrdinalIgnoreCase))
                .Where(a => String.IsNullOrEmpty(name) || AlertName(a).Contains(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.State == "firing" ? 0 : 1)
                .ThenBy(a => AlertName(a), StringComparer.Ordinal)
                .ToList();
            int cap = Math.Max(1, Math.Min(settings.MaxSeries, context.Limits.MaxRows));
            List<PromAlert> shown = alerts.Take(cap).ToList();
            int omitted = alerts.Count - shown.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(alerts.Count.ToString(CultureInfo.InvariantCulture)).Append(" alert(s)");
            if (omitted > 0) sb.Append(", showing ").Append(shown.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
            if (shown.Count > 0)
            {
                List<IList<string?>> rows = shown.Select(a => (IList<string?>)new List<string?>
                {
                    AlertName(a),
                    a.State,
                    a.ActiveAt.HasValue ? ObservabilityTime.Format(a.ActiveAt.Value) : String.Empty,
                    a.Value,
                    PromResultRenderer.LabelSet(a.Labels.Where(kv => kv.Key != "alertname").ToDictionary(kv => kv.Key, kv => kv.Value)),
                    a.Annotations.TryGetValue("summary", out string? s) ? s : (a.Annotations.TryGetValue("description", out string? d) ? d : String.Empty)
                }).ToList();
                sb.Append(TextTable.Render(new List<string> { "alertname", "state", "activeAt", "value", "labels", "summary" }, rows, 300));
            }
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object>
            {
                { "count", alerts.Count },
                { "omitted", omitted },
                { "alerts", shown }
            });
            ToolResult result = Finish(sb.ToString(), structured, context.Limits, omitted > 0);
            result.Metrics["alerts"] = alerts.Count;
            return result;
        }

        #endregion

        #region Private-Methods

        private static string AlertName(PromAlert alert)
        {
            return alert.Labels.TryGetValue("alertname", out string? n) ? n : String.Empty;
        }

        #endregion
    }
}
