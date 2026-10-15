namespace Hugo.Core.Responses
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Reachability of one observability service as probed by the server.
    /// </summary>
    public class ObservabilityServiceStatus
    {
        /// <summary>
        /// Service: grafana, prometheus, loki, or tempo.
        /// </summary>
        public string Service { get; set; } = String.Empty;

        /// <summary>
        /// Whether a URL is configured for the service.
        /// </summary>
        public bool Configured { get; set; } = false;

        /// <summary>
        /// Whether the readiness probe answered with a 2xx status. False when not configured.
        /// </summary>
        public bool Reachable { get; set; } = false;

        /// <summary>
        /// Version reported by the service, or null when unknown.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Version { get; set; } = null;

        /// <summary>
        /// When the probe ran (UTC).
        /// </summary>
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;
    }
}
