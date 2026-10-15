namespace Hugo.Connectors.Logging
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of SolarWinds Observability <c>GET /v1/metrics/{name}/measurements</c>.
    /// </summary>
    public class SwoMeasurementsResponse
    {
        /// <summary>
        /// Groupings. May be null when the service omits it.
        /// </summary>
        [JsonPropertyName("groupings")]
        public List<SwoGrouping>? Groupings { get; set; } = new List<SwoGrouping>();

        /// <summary>
        /// Bucket size used for the time series, in seconds.
        /// </summary>
        [JsonPropertyName("bucketSizeInSeconds")]
        public int BucketSizeInSeconds { get; set; } = 0;

        /// <summary>
        /// Paging information, or null.
        /// </summary>
        [JsonPropertyName("pageInfo")]
        public SwoPageInfo? PageInfo { get; set; } = null;
    }
}
