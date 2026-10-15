namespace Hugo.Connectors.Observability
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of <c>GET /</c> on Elasticsearch and OpenSearch.
    /// </summary>
    public class EsRootInfo
    {
        /// <summary>
        /// Node name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Cluster name.
        /// </summary>
        [JsonPropertyName("cluster_name")]
        public string? ClusterName { get; set; } = null;

        /// <summary>
        /// Version information.
        /// </summary>
        public EsVersionInfo? Version { get; set; } = null;

        /// <summary>
        /// Tagline.
        /// </summary>
        public string? Tagline { get; set; } = null;
    }
}
