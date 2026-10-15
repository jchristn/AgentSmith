namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured result of <c>logs_volume</c>.
    /// </summary>
    public class LogVolumePayload
    {
        /// <summary>
        /// Connector type.
        /// </summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = String.Empty;

        /// <summary>
        /// Query, or null for all events.
        /// </summary>
        [JsonPropertyName("query")]
        public string? Query { get; set; } = null;

        /// <summary>
        /// Window start, UTC.
        /// </summary>
        [JsonPropertyName("startTime")]
        public DateTime StartTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Window end, UTC.
        /// </summary>
        [JsonPropertyName("endTime")]
        public DateTime EndTime { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Bucket width in seconds.
        /// </summary>
        [JsonPropertyName("bucketSeconds")]
        public double BucketSeconds { get; set; } = 0;

        /// <summary>
        /// Total events counted.
        /// </summary>
        [JsonPropertyName("total")]
        public long Total { get; set; } = 0;

        /// <summary>
        /// Requests made to the provider.
        /// </summary>
        [JsonPropertyName("pages")]
        public int Pages { get; set; } = 0;

        /// <summary>
        /// Whether the whole window was scanned.
        /// </summary>
        [JsonPropertyName("complete")]
        public bool Complete { get; set; } = true;

        /// <summary>
        /// When incomplete, the oldest time covered by the scan, UTC.
        /// </summary>
        [JsonPropertyName("scannedFrom")]
        public DateTime? ScannedFrom { get; set; } = null;

        /// <summary>
        /// Buckets, oldest first.
        /// </summary>
        [JsonPropertyName("buckets")]
        public List<LogVolumeBucket> Buckets { get; set; } = new List<LogVolumeBucket>();
    }
}
