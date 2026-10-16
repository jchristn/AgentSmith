namespace Hugo.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Telemetry settings (Radiant).
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether telemetry is enabled. Default true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Service name. Default hugo-server.
        /// </summary>
        public string ServiceName { get; set; } = "hugo-server";

        /// <summary>
        /// Whether the Prometheus scrape listener is enabled. Default true.
        /// </summary>
        public bool PrometheusEnable { get; set; } = true;

        /// <summary>
        /// Prometheus listener hostname. Default *.
        /// </summary>
        public string PrometheusHostname { get; set; } = "*";

        /// <summary>
        /// Prometheus listener port. Minimum 1, maximum 65535, default 9464.
        /// </summary>
        public int PrometheusPort
        {
            get
            {
                return _PrometheusPort;
            }
            set
            {
                _PrometheusPort = Math.Clamp(value, 1, 65535);
            }
        }

        /// <summary>
        /// Prometheus path. Default /metrics.
        /// </summary>
        public string PrometheusPath { get; set; } = "/metrics";

        /// <summary>
        /// Whether OTLP export is enabled. Default false.
        /// </summary>
        public bool OtlpEnable { get; set; } = false;

        /// <summary>
        /// OTLP endpoint. Default http://127.0.0.1:4317.
        /// </summary>
        public string OtlpEndpoint { get; set; } = "http://127.0.0.1:4317";

        /// <summary>
        /// Whether GET /metrics is also served from the process's main listener (the server's REST port 8800, or the
        /// worker's health port 8710), relayed from the Prometheus listener. The Prometheus listener binds the pod
        /// host name, so kubectl port-forward and in-pod 127.0.0.1 requests cannot reach it; the main listener binds all
        /// interfaces. The relayed endpoint needs no authentication. Default true.
        /// </summary>
        public bool ServeMetricsOnServicePort { get; set; } = true;

        /// <summary>
        /// Distinct values each metric label (tenant, agent, deployment, model, and so on) may take before new values are
        /// reported as <c>other</c> and counted in hugo.telemetry.labels_folded. Bounds the number of series a
        /// busy cluster can create. Minimum 10, maximum 10000, default 200.
        /// </summary>
        public int MaxLabelValues
        {
            get
            {
                return _MaxLabelValues;
            }
            set
            {
                _MaxLabelValues = Math.Clamp(value, 10, 10000);
            }
        }

        /// <summary>
        /// Whether duration histograms carry trace exemplars (the trace that produced a sample), so Grafana panels can
        /// jump to the trace. Sets OTEL_METRICS_EXEMPLAR_FILTER=trace_based unless that variable is already set. Only
        /// OpenMetrics scrapes include exemplars, and Prometheus needs --enable-feature=exemplar-storage. Default true.
        /// </summary>
        public bool Exemplars { get; set; } = true;

        /// <summary>Whether scrubbed server/library entries enter Radiant log exporters. Default true.</summary>
        public bool LogsEnable { get; set; } = true;

        /// <summary>Whether logs also go directly to Loki over OTLP-HTTP. Default false.</summary>
        public bool LokiEnable { get; set; } = false;

        /// <summary>Loki OTLP base endpoint; /v1/logs is appended. Default http://127.0.0.1:3100/otlp.</summary>
        public string LokiEndpoint { get; set; } = "http://127.0.0.1:3100/otlp";

        /// <summary>Loki X-Scope-OrgID header, or empty for single-tenant Loki. Default empty.</summary>
        public string LokiTenantId { get; set; } = String.Empty;

        /// <summary>Bounded log bridge queue size. Default 1024, minimum 1, maximum 65536. Full queues drop new entries.</summary>
        public int LogQueueCapacity { get { return _LogQueueCapacity; } set { _LogQueueCapacity = Math.Clamp(value, 1, 65536); } }

        /// <summary>Maximum exported message/details characters per entry. Default 32768, minimum 256, maximum 1048576.</summary>
        public int LogMaxCharacters { get { return _LogMaxCharacters; } set { _LogMaxCharacters = Math.Clamp(value, 256, 1048576); } }

        /// <summary>Per-export network timeout. Default 3000 milliseconds, minimum 1000, maximum 30000.</summary>
        public int ExportTimeoutMs { get { return _ExportTimeoutMs; } set { _ExportTimeoutMs = Math.Clamp(value, 1000, 30000); } }

        /// <summary>Log bridge shutdown drain timeout. Default 5000 milliseconds, minimum 100, maximum 30000.</summary>
        public int LogDrainTimeoutMs { get { return _LogDrainTimeoutMs; } set { _LogDrainTimeoutMs = Math.Clamp(value, 100, 30000); } }

        #endregion

        #region Private-Members

        private int _PrometheusPort = 9464;
        private int _MaxLabelValues = 200;
        private int _LogQueueCapacity = 1024;
        private int _LogMaxCharacters = 32768;
        private int _ExportTimeoutMs = 3000;
        private int _LogDrainTimeoutMs = 5000;

        #endregion
    }
}
