namespace Hugo.Connectors.Observability
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One row of <c>GET _cat/indices?format=json&amp;bytes=b</c>. Numbers arrive as strings.
    /// </summary>
    public class EsCatIndex
    {
        /// <summary>
        /// Health: green, yellow, or red.
        /// </summary>
        public string? Health { get; set; } = null;

        /// <summary>
        /// Status: open or close.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Index name.
        /// </summary>
        public string? Index { get; set; } = null;

        /// <summary>
        /// Document count.
        /// </summary>
        [JsonPropertyName("docs.count")]
        public string? DocsCount { get; set; } = null;

        /// <summary>
        /// Store size in bytes.
        /// </summary>
        [JsonPropertyName("store.size")]
        public string? StoreSize { get; set; } = null;

        /// <summary>
        /// Primary shards.
        /// </summary>
        public string? Pri { get; set; } = null;

        /// <summary>
        /// Replicas.
        /// </summary>
        public string? Rep { get; set; } = null;
    }
}
