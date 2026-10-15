namespace Hugo.Sdk.Models
{
    /// <summary>
    /// Links to the configured observability services.
    /// </summary>
    public class ObservabilityLinks
    {
        #region Public-Members

        /// <summary>
        /// Grafana URL.
        /// </summary>
        public string? GrafanaUrl { get; set; } = null;

        /// <summary>
        /// Prometheus URL.
        /// </summary>
        public string? PrometheusUrl { get; set; } = null;

        /// <summary>
        /// Loki URL.
        /// </summary>
        public string? LokiUrl { get; set; } = null;

        /// <summary>
        /// Tempo URL.
        /// </summary>
        public string? TempoUrl { get; set; } = null;

        #endregion
    }
}
