namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// Search engine behind an <c>elasticsearch</c> resource. Both speak the same REST API for the tools Hugo uses.
    /// </summary>
    public enum ElasticsearchFlavorEnum
    {
        /// <summary>
        /// Elastic's Elasticsearch (7.x or 8.x and later).
        /// </summary>
        Elasticsearch = 0,

        /// <summary>
        /// OpenSearch (1.x or later).
        /// </summary>
        OpenSearch = 1
    }
}
