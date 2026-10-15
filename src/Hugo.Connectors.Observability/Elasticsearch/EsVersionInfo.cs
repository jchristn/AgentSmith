namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// Version block of <c>GET /</c>.
    /// </summary>
    public class EsVersionInfo
    {
        /// <summary>
        /// Version number.
        /// </summary>
        public string? Number { get; set; } = null;

        /// <summary>
        /// Distribution: <c>opensearch</c> for OpenSearch, absent for Elasticsearch.
        /// </summary>
        public string? Distribution { get; set; } = null;
    }
}
