namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One Elasticsearch error object.
    /// </summary>
    public class EsErrorDetail
    {
        /// <summary>
        /// Error type (for example <c>index_not_found_exception</c>).
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Root causes.
        /// </summary>
        [JsonPropertyName("root_cause")]
        public List<EsErrorDetail>? RootCause { get; set; } = null;
    }
}
