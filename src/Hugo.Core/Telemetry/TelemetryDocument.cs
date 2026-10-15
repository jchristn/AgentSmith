namespace Hugo.Core.Telemetry
{
    using System;
    using System.Linq;
    using System.Text;
    using Radiant;

    /// <summary>
    /// Generates docs/TELEMETRY.md from <see cref="HugoMetricCatalog"/> (P1.36). A test compares the checked-in file
    /// with this output, so the document cannot drift from the instruments. Thread-safe (stateless).
    /// </summary>
    public static class TelemetryDocument
    {
        #region Public-Methods

        /// <summary>
        /// Render the full document.
        /// </summary>
        /// <returns>Markdown text with LF line endings.</returns>
        public static string Render()
        {
            StringBuilder text = new StringBuilder();
            text.Append("# Telemetry\n\n");
            text.Append("This file is generated from `HugoMetricCatalog` (`src/Hugo.Core/Telemetry`). Do not edit it by hand: run the test suites with `HUGO_WRITE_DOCS=true` to regenerate it, and the `telemetry-document` test fails while it is out of date.\n\n");
            text.Append("Hugo records metrics through the `Hugo` meter and spans through the `Hugo` activity source. Watson adds its own HTTP server metrics and one span per request. Radiant exports metrics to Prometheus (the `/metrics` scrape) and traces to OTLP when `Telemetry.OtlpEnable` is true. Label values are bounded by `LabelGuard`; identifiers never appear in labels.\n\n");
            text.Append("## Metrics\n\n");
            text.Append(MetricsTable());
            return text.ToString();
        }

        /// <summary>
        /// Render the metrics table.
        /// </summary>
        /// <returns>Markdown table.</returns>
        public static string MetricsTable()
        {
            StringBuilder text = new StringBuilder();
            text.Append("| Metric (Prometheus) | Type | Labels | Category | Meaning |\n");
            text.Append("|---|---|---|---|---|\n");
            foreach (MetricDescriptor metric in HugoMetricCatalog.All().OrderBy(m => m.Category, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal))
            {
                text.Append("| `").Append(metric.PrometheusName).Append("` | ").Append(Kind(metric.Kind)).Append(" | ")
                    .Append(metric.Labels.Count == 0 ? "none" : String.Join(", ", metric.Labels.Select(l => "`" + l + "`"))).Append(" | ")
                    .Append(metric.Category).Append(" | ")
                    .Append(metric.Description.Replace("|", "/")).Append(" |\n");
            }
            return text.ToString();
        }

        #endregion

        #region Private-Methods

        private static string Kind(MetricKindEnum kind)
        {
            switch (kind)
            {
                case MetricKindEnum.Counter:
                    return "counter";
                case MetricKindEnum.Histogram:
                    return "histogram";
                case MetricKindEnum.UpDownCounter:
                    return "gauge (up/down)";
                default:
                    return "gauge";
            }
        }

        #endregion
    }
}
