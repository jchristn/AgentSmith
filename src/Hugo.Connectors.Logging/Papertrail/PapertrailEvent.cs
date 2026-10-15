namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One event in a Papertrail search response (<c>events[]</c> of <c>GET /events/search.json</c>).
    /// </summary>
    public class PapertrailEvent
    {
        /// <summary>
        /// Event ID: a 64-bit integer serialized as a string. Increases with time but is not sequential.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// When the event was generated (ISO 8601 with offset).
        /// </summary>
        [JsonPropertyName("generated_at")]
        public DateTimeOffset? GeneratedAt { get; set; } = null;

        /// <summary>
        /// When Papertrail received the event (ISO 8601, in the token owner's time zone).
        /// </summary>
        [JsonPropertyName("received_at")]
        public DateTimeOffset? ReceivedAt { get; set; } = null;

        /// <summary>
        /// Receipt time formatted for display.
        /// </summary>
        [JsonPropertyName("display_received_at")]
        public string? DisplayReceivedAt { get; set; } = null;

        /// <summary>
        /// Sender (system) name.
        /// </summary>
        [JsonPropertyName("source_name")]
        public string? SourceName { get; set; } = null;

        /// <summary>
        /// Sender (system) ID.
        /// </summary>
        [JsonPropertyName("source_id")]
        public long SourceId { get; set; } = 0;

        /// <summary>
        /// Hostname in the message.
        /// </summary>
        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// Originating IP address.
        /// </summary>
        [JsonPropertyName("source_ip")]
        public string? SourceIp { get; set; } = null;

        /// <summary>
        /// Syslog facility.
        /// </summary>
        [JsonPropertyName("facility")]
        public string? Facility { get; set; } = null;

        /// <summary>
        /// Syslog severity.
        /// </summary>
        [JsonPropertyName("severity")]
        public string? Severity { get; set; } = null;

        /// <summary>
        /// Syslog tag. May be null.
        /// </summary>
        [JsonPropertyName("program")]
        public string? Program { get; set; } = null;

        /// <summary>
        /// Message body. May be empty.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        /// <summary>
        /// Convert to the provider-neutral shape. The timestamp is the receipt time (the time Papertrail filters on).
        /// </summary>
        /// <returns>Event.</returns>
        public LogEvent ToLogEvent()
        {
            DateTimeOffset? time = ReceivedAt ?? GeneratedAt;
            return new LogEvent
            {
                Id = Id ?? String.Empty,
                Timestamp = time.HasValue ? time.Value.UtcDateTime : DateTime.MinValue,
                Host = !String.IsNullOrEmpty(SourceName) ? SourceName : (Hostname ?? String.Empty),
                Program = Program ?? String.Empty,
                Severity = Severity ?? String.Empty,
                Message = Message ?? String.Empty
            };
        }
    }
}
