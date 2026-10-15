namespace Hugo.Connectors.Logging
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of SolarWinds Observability <c>GET /v1/entities</c>.
    /// </summary>
    public class SwoEntitiesResponse
    {
        /// <summary>
        /// Matching entities. May be null when the service omits it.
        /// </summary>
        [JsonPropertyName("entities")]
        public List<SwoEntity>? Entities { get; set; } = new List<SwoEntity>();

        /// <summary>
        /// Paging information, or null.
        /// </summary>
        [JsonPropertyName("pageInfo")]
        public SwoPageInfo? PageInfo { get; set; } = null;
    }
}
