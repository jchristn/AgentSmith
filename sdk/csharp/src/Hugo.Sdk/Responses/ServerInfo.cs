namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Server version, endpoints, and feature flags.
    /// </summary>
    public class ServerInfo
    {
        #region Public-Members

        /// <summary>
        /// Product.
        /// </summary>
        public string Product { get; set; } = String.Empty;

        /// <summary>
        /// Version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// Node Identifier.
        /// </summary>
        public string? NodeId { get; set; } = null;

        /// <summary>
        /// Rest URL.
        /// </summary>
        public string? RestUrl { get; set; } = null;

        /// <summary>
        /// MCP URL.
        /// </summary>
        public string? McpUrl { get; set; } = null;

        /// <summary>
        /// A2A base URL.
        /// </summary>
        [JsonPropertyName("a2aUrl")]
        public string? A2AUrl { get; set; } = null;

        /// <summary>
        /// Open API URL.
        /// </summary>
        public string? OpenApiUrl { get; set; } = null;

        /// <summary>
        /// Database type.
        /// </summary>
        public string? DatabaseType { get; set; } = null;

        /// <summary>
        /// Telemetry enabled.
        /// </summary>
        public bool TelemetryEnabled { get; set; } = false;

        /// <summary>
        /// Request history enabled.
        /// </summary>
        public bool RequestHistoryEnabled { get; set; } = false;

        /// <summary>
        /// Route count.
        /// </summary>
        public int RouteCount { get; set; } = 0;

        /// <summary>
        /// Repository URL.
        /// </summary>
        public string? RepositoryUrl { get; set; } = null;

        /// <summary>
        /// Started UTC.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Features.
        /// </summary>
        public List<string> Features { get; set; } = new List<string>();

        /// <summary>
        /// Observability.
        /// </summary>
        public ObservabilityLinks? Observability { get; set; } = null;

        #endregion
    }
}
