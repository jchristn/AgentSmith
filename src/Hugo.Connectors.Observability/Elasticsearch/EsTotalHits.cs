namespace Hugo.Connectors.Observability
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// <c>hits.total</c>: an object <c>{"value":n,"relation":"eq"}</c>, or a plain number on older servers.
    /// </summary>
    [JsonConverter(typeof(EsTotalHitsConverter))]
    public class EsTotalHits
    {
        /// <summary>
        /// Count.
        /// </summary>
        public long Value { get; set; } = 0;

        /// <summary>
        /// <c>eq</c> when exact, <c>gte</c> when a lower bound.
        /// </summary>
        public string Relation { get; set; } = "eq";
    }
}
