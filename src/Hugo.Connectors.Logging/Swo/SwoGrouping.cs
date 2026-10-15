namespace Hugo.Connectors.Logging
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Measurements for one combination of group-by attributes (<c>Metrics.Grouping</c>).
    /// </summary>
    public class SwoGrouping
    {
        /// <summary>
        /// Attributes identifying the grouping, or null.
        /// </summary>
        [JsonPropertyName("attributes")]
        public List<SwoKeyValuePair>? Attributes { get; set; } = null;

        /// <summary>
        /// Data points; empty when no data exists, or null.
        /// </summary>
        [JsonPropertyName("measurements")]
        public List<SwoMeasurement>? Measurements { get; set; } = null;
    }
}
