namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One time bucket of <c>logs_volume</c>.
    /// </summary>
    public class LogVolumeBucket
    {
        /// <summary>
        /// Inclusive bucket start, UTC.
        /// </summary>
        [JsonPropertyName("start")]
        public DateTime Start { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Exclusive bucket end, UTC (inclusive for the last bucket).
        /// </summary>
        [JsonPropertyName("end")]
        public DateTime End { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Matching events counted in the bucket.
        /// </summary>
        [JsonPropertyName("count")]
        public long Count { get; set; } = 0;

        /// <summary>
        /// False when the scan stopped before covering the whole bucket, so the count is a lower bound.
        /// </summary>
        [JsonPropertyName("complete")]
        public bool Complete { get; set; } = true;
    }
}
