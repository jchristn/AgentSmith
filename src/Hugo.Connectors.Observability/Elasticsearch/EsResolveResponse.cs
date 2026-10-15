namespace Hugo.Connectors.Observability
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Response of <c>GET _resolve/index/{expression}</c>: the indexes, aliases, and data streams an index expression
    /// names or expands to.
    /// </summary>
    public class EsResolveResponse
    {
        /// <summary>
        /// Concrete indexes. Never null after deserialization of a well-formed response.
        /// </summary>
        public List<EsResolvedIndex> Indices { get; set; } = new List<EsResolvedIndex>();

        /// <summary>
        /// Aliases and the indexes or data streams behind each.
        /// </summary>
        public List<EsResolvedAlias> Aliases { get; set; } = new List<EsResolvedAlias>();

        /// <summary>
        /// Data streams.
        /// </summary>
        [JsonPropertyName("data_streams")]
        public List<EsResolvedDataStream> DataStreams { get; set; } = new List<EsResolvedDataStream>();
    }
}
