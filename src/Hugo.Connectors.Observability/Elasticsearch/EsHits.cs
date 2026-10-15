namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The <c>hits</c> block of a search response.
    /// </summary>
    public class EsHits
    {
        /// <summary>
        /// Total matches.
        /// </summary>
        public EsTotalHits? Total { get; set; } = null;

        /// <summary>
        /// Highest score.
        /// </summary>
        [JsonPropertyName("max_score")]
        public double? MaxScore { get; set; } = null;

        /// <summary>
        /// Hits.
        /// </summary>
        public List<EsHit> Hits { get; set; } = new List<EsHit>();
    }
}
