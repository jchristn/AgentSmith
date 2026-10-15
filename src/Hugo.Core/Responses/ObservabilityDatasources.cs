namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Grafana datasource UIDs used to build deep links (Phase 13 contract).
    /// </summary>
    public class ObservabilityDatasources
    {
        /// <summary>
        /// Prometheus datasource UID. Default hugo-prometheus.
        /// </summary>
        public string Prometheus { get; set; } = "hugo-prometheus";

        /// <summary>
        /// Loki datasource UID. Default hugo-loki.
        /// </summary>
        public string Loki { get; set; } = "hugo-loki";

        /// <summary>
        /// Tempo datasource UID. Default hugo-tempo.
        /// </summary>
        public string Tempo { get; set; } = "hugo-tempo";
    }
}
