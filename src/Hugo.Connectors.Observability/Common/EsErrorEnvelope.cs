namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// Error body of the Elasticsearch and OpenSearch REST APIs: <c>{"error":{"type":"...","reason":"..."},"status":400}</c>.
    /// </summary>
    public class EsErrorEnvelope
    {
        /// <summary>
        /// Error detail.
        /// </summary>
        public EsErrorDetail? Error { get; set; } = null;

        /// <summary>
        /// HTTP status echoed in the body.
        /// </summary>
        public int? Status { get; set; } = null;
    }
}
