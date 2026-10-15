namespace Hugo.Connectors.Logging
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of SolarWinds Observability <c>GET /v1/logs</c>.
    /// </summary>
    public class SwoLogsResponse
    {
        /// <summary>
        /// Events. May be null when the service omits it.
        /// </summary>
        [JsonPropertyName("logs")]
        public List<SwoLogEvent>? Logs { get; set; } = new List<SwoLogEvent>();

        /// <summary>
        /// Paging information, or null.
        /// </summary>
        [JsonPropertyName("pageInfo")]
        public SwoPageInfo? PageInfo { get; set; } = null;
    }
}
