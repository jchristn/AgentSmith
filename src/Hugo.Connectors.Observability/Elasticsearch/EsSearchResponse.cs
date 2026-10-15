namespace Hugo.Connectors.Observability
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of <c>POST {index}/_search</c>. Aggregations are free-form and kept as an element.
    /// </summary>
    public class EsSearchResponse
    {
        /// <summary>
        /// Milliseconds the search took.
        /// </summary>
        public long Took { get; set; } = 0;

        /// <summary>
        /// Whether the search timed out on some shards.
        /// </summary>
        [JsonPropertyName("timed_out")]
        public bool TimedOut { get; set; } = false;

        /// <summary>
        /// Hits.
        /// </summary>
        public EsHits Hits { get; set; } = new EsHits();

        /// <summary>
        /// Aggregation results.
        /// </summary>
        public JsonElement? Aggregations { get; set; } = null;
    }
}
