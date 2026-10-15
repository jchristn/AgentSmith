namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One event of SolarWinds Observability <c>GET /v1/logs</c> (<c>Logs.Event</c>).
    /// </summary>
    public class SwoLogEvent
    {
        /// <summary>
        /// Event ID.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// Event time.
        /// </summary>
        [JsonPropertyName("time")]
        public DateTimeOffset? Time { get; set; } = null;

        /// <summary>
        /// Message.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        /// <summary>
        /// Host name.
        /// </summary>
        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// Severity.
        /// </summary>
        [JsonPropertyName("severity")]
        public string? Severity { get; set; } = null;

        /// <summary>
        /// Program name.
        /// </summary>
        [JsonPropertyName("program")]
        public string? Program { get; set; } = null;

        /// <summary>
        /// Convert to the provider-neutral shape.
        /// </summary>
        /// <returns>Event.</returns>
        public LogEvent ToLogEvent()
        {
            return new LogEvent
            {
                Id = Id ?? String.Empty,
                Timestamp = Time.HasValue ? Time.Value.UtcDateTime : DateTime.MinValue,
                Host = Hostname ?? String.Empty,
                Program = Program ?? String.Empty,
                Severity = Severity ?? String.Empty,
                Message = Message ?? String.Empty
            };
        }
    }
}
