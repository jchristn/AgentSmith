namespace Hugo.Connectors.Observability
{
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One search hit. The document source is free-form and kept as an element.
    /// </summary>
    public class EsHit
    {
        /// <summary>
        /// Index.
        /// </summary>
        [JsonPropertyName("_index")]
        public string? Index { get; set; } = null;

        /// <summary>
        /// Document ID.
        /// </summary>
        [JsonPropertyName("_id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Score, or null when sorting.
        /// </summary>
        [JsonPropertyName("_score")]
        public double? Score { get; set; } = null;

        /// <summary>
        /// Document source.
        /// </summary>
        [JsonPropertyName("_source")]
        public JsonElement? Source { get; set; } = null;

        /// <summary>
        /// Sort values, for <c>search_after</c> paging.
        /// </summary>
        public JsonElement? Sort { get; set; } = null;
    }
}
