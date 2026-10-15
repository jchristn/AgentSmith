namespace Hugo.Server.Services
{
    using System;

    /// <summary>
    /// Version field of a service health or build information response (Grafana /api/health, Loki and Tempo build info,
    /// and the data object of Prometheus build info).
    /// </summary>
    public class BuildInfoVersion
    {
        /// <summary>
        /// Reported version, or null when absent.
        /// </summary>
        public string? Version { get; set; } = null;
    }
}
