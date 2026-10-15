namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// Response of <c>POST {index}/_count</c>.
    /// </summary>
    public class EsCountResponse
    {
        /// <summary>
        /// Matching documents.
        /// </summary>
        public long Count { get; set; } = 0;
    }
}
