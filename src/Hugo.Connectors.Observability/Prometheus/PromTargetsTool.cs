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
    /// <c>prom_targets</c>: list scrape targets with health and last error. Read. Thread-safe.
    /// </summary>
    public class PromTargetsTool : PromToolBase
    {
        #region Public-Members

        /// <summary>
        /// Input schema.
        /// </summary>
        public static readonly string InputSchema =
            "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{"
            + "\"state\":{\"type\":\"string\",\"enum\":[\"active\",\"dropped\",\"any\"],\"description\":\"Active (scraped) or dropped (removed by relabeling) targets. Default active.\"},"
            + "\"job\":{\"type\":\"string\",\"description\":\"Only targets whose job or scrape pool contains this text (case-insensitive).\"},"
            + "\"health\":{\"type\":\"string\",\"enum\":[\"up\",\"down\",\"unknown\"],\"description\":\"Only active targets with this health.\"}"
            + "}}";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromTargetsTool() : base(new ToolDescriptor
        {
            Name = "prom_targets",
            Title = "Prometheus scrape targets",
            Description = "List scrape targets with job, instance, health, last scrape, duration, and last error. Use it when metrics are missing or stale: a down target explains gaps.",
            InputSchemaJson = InputSchema,
            BaseEffect = EffectEnum.Read,
            OutputDescription = "Table of job, instance, health, lastScrape, durationMs, lastError plus structured JSON {count, omitted, targets[]}."
        })
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<ToolResult> ExecuteCoreAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token)
        {
            PrometheusSettings settings = ReadSettings(context);
            string state = (arguments.GetString("state", "active") ?? "active").Trim().ToLowerInvariant();
            if (state != "active" && state != "dropped" && state != "any")
                throw new ConnectorException(ConnectorErrorCodes.InvalidArgument, "Argument 'state' must be active, dropped, or any.");
            string? job = arguments.GetString("job");
            string? health = arguments.GetString("health");

            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>> { new KeyValuePair<string, string?>("state", state) };
            PromResponse<PromTargetsData> response = await Client(context, settings).GetAsync<PromTargetsData>("api/v1/targets", q, token).ConfigureAwait(false);
            List<PromTarget> all = new List<PromTarget>();
            if (state != "dropped" && response.Data!.ActiveTargets != null) all.AddRange(response.Data.ActiveTargets);
            if (state != "active" && response.Data!.DroppedTargets != null) all.AddRange(response.Data.DroppedTargets);

            List<PromTarget> filtered = all
                .Where(t => String.IsNullOrEmpty(job) || Job(t).Contains(job, StringComparison.OrdinalIgnoreCase))
                .Where(t => String.IsNullOrEmpty(health) || String.Equals(t.Health, health, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Health == "down" ? 0 : 1)
                .ThenBy(t => Job(t), StringComparer.Ordinal)
                .ThenBy(t => Instance(t), StringComparer.Ordinal)
                .ToList();
            int cap = Math.Max(1, Math.Min(settings.MaxSeries, context.Limits.MaxRows));
            List<PromTarget> shown = filtered.Take(cap).ToList();
            int omitted = filtered.Count - shown.Count;

            StringBuilder sb = new StringBuilder();
            sb.Append(filtered.Count.ToString(CultureInfo.InvariantCulture)).Append(" target(s)");
            int down = filtered.Count(t => t.Health == "down");
            if (down > 0) sb.Append(", ").Append(down.ToString(CultureInfo.InvariantCulture)).Append(" down");
            if (omitted > 0) sb.Append(", showing ").Append(shown.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append('\n');
            if (shown.Count > 0)
            {
                List<IList<string?>> rows = shown.Select(t => (IList<string?>)new List<string?>
                {
                    Job(t),
                    Instance(t),
                    t.Health ?? "dropped",
                    t.LastScrape.HasValue ? ObservabilityTime.Format(t.LastScrape.Value) : String.Empty,
                    t.LastScrapeDuration.HasValue ? (t.LastScrapeDuration.Value * 1000).ToString("0.#", CultureInfo.InvariantCulture) : String.Empty,
                    t.LastError ?? String.Empty
                }).ToList();
                sb.Append(TextTable.Render(new List<string> { "job", "instance", "health", "lastScrape", "durationMs", "lastError" }, rows, 300));
            }
            string structured = ObservabilityJson.Serialize(new Dictionary<string, object>
            {
                { "count", filtered.Count },
                { "omitted", omitted },
                { "targets", shown.Select(t => new Dictionary<string, object?> { { "job", Job(t) }, { "instance", Instance(t) }, { "scrapeUrl", t.ScrapeUrl }, { "health", t.Health }, { "lastScrape", t.LastScrape }, { "lastScrapeDuration", t.LastScrapeDuration }, { "lastError", t.LastError } }).ToList() }
            });
            ToolResult result = Finish(sb.ToString(), structured, context.Limits, omitted > 0);
            result.Metrics["targets"] = filtered.Count;
            result.Metrics["down"] = down;
            return result;
        }

        #endregion

        #region Private-Methods

        private static string Job(PromTarget t)
        {
            if (t.Labels != null && t.Labels.TryGetValue("job", out string? j)) return j;
            if (!String.IsNullOrEmpty(t.ScrapePool)) return t.ScrapePool;
            if (t.DiscoveredLabels != null && t.DiscoveredLabels.TryGetValue("job", out string? d)) return d;
            return String.Empty;
        }

        private static string Instance(PromTarget t)
        {
            if (t.Labels != null && t.Labels.TryGetValue("instance", out string? i)) return i;
            if (t.DiscoveredLabels != null && t.DiscoveredLabels.TryGetValue("__address__", out string? a)) return a;
            return t.ScrapeUrl ?? String.Empty;
        }

        #endregion
    }
}
