namespace Hugo.Connectors.Observability
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One log line of a Loki stream, serialized by the API as <c>["unixNanoseconds", "line"]</c> (optionally with a
    /// third structured metadata object, which is ignored).
    /// </summary>
    [JsonConverter(typeof(LokiEntryConverter))]
    public class LokiEntry
    {
        /// <summary>
        /// Timestamp in Unix nanoseconds, as text.
        /// </summary>
        public string Timestamp { get; set; } = "0";

        /// <summary>
        /// Log line.
        /// </summary>
        public string Line { get; set; } = String.Empty;
    }
}
