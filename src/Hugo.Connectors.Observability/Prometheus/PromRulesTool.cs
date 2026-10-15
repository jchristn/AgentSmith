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
    /// <c>prom_rules</c>: list alerting and recording rules with their health. Read. Thread-safe.
    /// </summary>
    public class PromRulesTool : PromToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"type\":{\"type\":\"string\",\"enum\":[\"alert\",\"record\"],\"description\":\"Only alerting (alert) or recording (record) rules. Default both.\"},"
            + "\"group\":{\"type\":\"string\",\"description\":\"Only groups whose name contains this text (case-insensitive).\"},"
            + "\"name\":{\"type\":\"string\",\"description\":\"Only rules whose name contains this text (case-insensitive).\"},"
            + "\"unhealthyOnly\":{\"type\":\"boolean\",\"description\":\"Only rules whose last evaluation failed. Default false.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromRulesTool() : base(new ToolDescriptor
        {
            Name = "prom_rules",
            Title = "Prometheus rules",
            Description = "List alerting and recording rules by group with expression, health, state, and last error. Use it to see what an alert means, or why it is not firing.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table of group, type, name, health, state, query, lastError plus structured JSON {count, omitted, rules[]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            PrometheusSettings settings = ReadSettings(context);
            string? type = arguments.GetString("type");
            if (!String.IsNullOrEmpty(type) && type != "alert" && type != "record")
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'type' must be alert or record.");
            string? group = arguments.GetString("group");
            string? name = arguments.GetString("name");
            bool unhealthy = arguments.GetBool("unhealthyOnly", false);

            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>> { new KeyValuePair<string, string?>("type", type) };
            PromResponse<PromRulesData> response = await Client(context, settings).GetAsync<PromRulesData>("api/v1/rules", q, token).ConfigureAwait(false);

            List<RuleRow> rows = new List<RuleRow>();
            foreach (PromRuleGroup g in response.Data!.Groups)
            {
                if (!String.IsNullOrEmpty(group) && !(g.Name ?? String.Empty).Contains(group, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (PromRule r in g.Rules)
                {
                    if (!String.IsNullOrEmpty(name) && !(r.Name ?? String.Empty).Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (unhealthy && String.Equals(r.Health, "ok", StringComparison.OrdinalIgnoreCase)) continue;
                    rows.Add(new RuleRow { Group = g.Name, Rule = r });
                }
            }
            int cap = Math.Max(1, context.Limits.MaxRows);
            List<RuleRow> shown = rows.Take(cap).ToList();
            int omitted = rows.Count - shown.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" rule(s)");
            if (omitted > 0) sb.Append(", showing ").Append(shown.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
            if (shown.Count > 0)
            {
                List<IList<string?>> table = shown.Select(x => (IList<string?>)new List<string?>
                {
                    x.Group,
                    x.Rule.Type,
                    x.Rule.Name,
                    x.Rule.Health,
                    x.Rule.State ?? String.Empty,
                    x.Rule.Query,
                    x.Rule.LastError ?? String.Empty
                }).ToList();
                sb.Append(TextTable.Render(new List<string> { "group", "type", "name", "health", "state", "query", "lastError" }, table, 300));
            }
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object>
            {
                { "count", rows.Count },
                { "omitted", omitted },
                { "rules", shown.Select(x => new Dictionary<string, object?> { { "group", x.Group }, { "name", x.Rule.Name }, { "type", x.Rule.Type }, { "health", x.Rule.Health }, { "state", x.Rule.State }, { "query", x.Rule.Query }, { "lastError", x.Rule.LastError }, { "activeAlerts", x.Rule.Alerts == null ? 0 : x.Rule.Alerts.Count } }).ToList() }
            });
            ToolResult result = Finish(sb.ToString(), structured, context.Limits, omitted > 0);
            result.Metrics["rules"] = rows.Count;
            return result;
        }

        #endregion
    }
}
