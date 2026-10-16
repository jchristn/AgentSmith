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
            text.Append("## Logs\n\n");
            text.Append("Every Hugo-managed server and library entry is scrubbed before console, file, syslog, log activities, or Radiant. Structured sensitive-key properties and exception text are scrubbed too. SyslogLogging has a post-write event, so Hugo uses a private ingress with console, file, syslog, metrics, and tracing disabled, then forwards only sanitized entries to the configured output module.\n\n");
            text.Append("`Telemetry.LogsEnable` enables a bounded, nonblocking bridge into Radiant. `Telemetry.OtlpEnable` sends logs to the collector alongside traces and metrics; `Telemetry.LokiEnable` optionally also sends OTLP-HTTP directly to `Telemetry.LokiEndpoint` (`/v1/logs` is appended). Captured trace/span context survives queuing, and ambient logging scopes are excluded. Queue overflow drops new entries; `HugoTelemetry.LogsDropped` reports bridge drops. The OpenTelemetry exporters also use their own bounded batch queues. Network failures do not fail requests, and shutdown drains the bridge before disposing exporters.\n\n");
            text.Append("Bundled Alloy forwards native server logs with `app=hugo`, `component=server`, `node`, `level`, and `pipeline=otlp`; trace/span IDs remain structured fields. Dashboard container logs retain their existing collection path. Server container logs are excluded to prevent duplicate ingestion. Lines emitted before Radiant starts remain in local console/file sinks. If you disable native log export, restore server container/pod collection in Alloy. Direct Loki export uses Loki's default `service_name` and `service_instance_id` resource labels instead of Alloy's label mapping.\n\n");
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
