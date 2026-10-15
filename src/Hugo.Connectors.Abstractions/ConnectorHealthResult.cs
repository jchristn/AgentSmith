namespace Hugo.Connectors.Abstractions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of a connection test.
    /// </summary>
    public class ConnectorHealthResult
    {
        /// <summary>
        /// Whether the probe succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Human-readable message (for example <c>Connected to PostgreSQL 16.4</c>). Never contains secrets.
        /// </summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>
        /// Stable error code when the probe failed.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Round-trip latency in milliseconds.
        /// </summary>
        public double LatencyMs { get; set; } = 0;

        /// <summary>
        /// Non-secret facts learned (for example server version, host key fingerprint). Never null.
        /// </summary>
        public Dictionary<string, string> Details { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Build a success result.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <param name="latencyMs">Latency.</param>
        /// <returns>Result.</returns>
        public static ConnectorHealthResult Ok(string message, double latencyMs)
        {
            return new ConnectorHealthResult { Success = true, Message = message ?? String.Empty, LatencyMs = latencyMs };
        }

        /// <summary>
        /// Build a failure result.
        /// </summary>
        /// <param name="code">Stable error code.</param>
        /// <param name="message">Message.</param>
        /// <param name="latencyMs">Latency.</param>
        /// <returns>Result.</returns>
        public static ConnectorHealthResult Fail(string code, string message, double latencyMs)
        {
            return new ConnectorHealthResult { Success = false, ErrorCode = code, Message = message ?? String.Empty, LatencyMs = latencyMs };
        }
    }
}
