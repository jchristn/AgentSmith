namespace Hugo.Core.Responses
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Browser-facing observability endpoints reported by server info. Null values mean not configured; they are
    /// always written so clients can tell "not configured" from "unknown".
    /// </summary>
    public class ObservabilityInfo
    {
        /// <summary>
        /// Grafana URL, or null when not configured.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? GrafanaUrl { get; set; } = null;

        /// <summary>
        /// Prometheus URL, or null when not configured.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? PrometheusUrl { get; set; } = null;

        /// <summary>
        /// Loki URL, or null when not configured.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? LokiUrl { get; set; } = null;

        /// <summary>
        /// Tempo URL, or null when not configured.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? TempoUrl { get; set; } = null;

        /// <summary>
        /// Grafana datasource UIDs, or null when Grafana is not configured.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public ObservabilityDatasources? Datasources { get; set; } = null;

        /// <summary>
        /// Grafana dashboard UIDs, or null when Grafana is not configured.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public ObservabilityDashboards? Dashboards { get; set; } = null;
    }
}
