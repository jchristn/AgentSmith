namespace Hugo.Core.Telemetry
{
    using System;

    /// <summary>
    /// Prometheus exposition text read from the process's own metrics listener, relayed on another listener.
    /// </summary>
    public class MetricsScrape
    {
        /// <summary>
        /// Exposition body.
        /// </summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>
        /// Content type reported by the metrics listener. Default text/plain; version=0.0.4; charset=utf-8.
        /// </summary>
        public string ContentType { get; set; } = "text/plain; version=0.0.4; charset=utf-8";
    }
}
