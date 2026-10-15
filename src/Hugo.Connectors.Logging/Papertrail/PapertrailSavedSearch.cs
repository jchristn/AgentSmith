namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A Papertrail saved search, from <c>GET /searches.json</c>.
    /// </summary>
    public class PapertrailSavedSearch
    {
        /// <summary>
        /// Saved search ID.
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; } = 0;

        /// <summary>
        /// Name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Query text.
        /// </summary>
        [JsonPropertyName("query")]
        public string? Query { get; set; } = null;

        /// <summary>
        /// Group scope, or null.
        /// </summary>
        [JsonPropertyName("group")]
        public PapertrailSavedSearchGroup? Group { get; set; } = null;
    }
}
