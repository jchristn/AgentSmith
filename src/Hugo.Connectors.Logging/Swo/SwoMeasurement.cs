namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One metric data point (<c>Metrics.Measurement</c>).
    /// </summary>
    public class SwoMeasurement
    {
        /// <summary>
        /// Bucket time.
        /// </summary>
        [JsonPropertyName("time")]
        public DateTimeOffset? Time { get; set; } = null;

        /// <summary>
        /// Value; null when the bucket has no data.
        /// </summary>
        [JsonPropertyName("value")]
        public double? Value { get; set; } = null;
    }
}
