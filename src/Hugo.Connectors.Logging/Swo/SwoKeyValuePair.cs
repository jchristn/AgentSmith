namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Attribute identifying a metric grouping (<c>Common.KeyValuePair</c>).
    /// </summary>
    public class SwoKeyValuePair
    {
        /// <summary>
        /// Key.
        /// </summary>
        [JsonPropertyName("key")]
        public string? Key { get; set; } = null;

        /// <summary>
        /// Value.
        /// </summary>
        [JsonPropertyName("value")]
        public string? Value { get; set; } = null;
    }
}
