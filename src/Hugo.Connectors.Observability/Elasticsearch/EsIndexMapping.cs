namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// One index entry of <c>GET {index}/_mapping</c>: <c>{"mappings":{"properties":{...}}}</c>.
    /// </summary>
    public class EsIndexMapping
    {
        /// <summary>
        /// Mappings.
        /// </summary>
        public EsFieldMapping? Mappings { get; set; } = null;
    }
}
