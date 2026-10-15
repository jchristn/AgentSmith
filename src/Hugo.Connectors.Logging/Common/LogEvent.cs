namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One log event in the provider-neutral shape returned by the <c>logs</c> family tools.
    /// </summary>
    public class LogEvent
    {
        /// <summary>
        /// Provider event identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Event time, UTC (Papertrail: received time; SolarWinds Observability: event time).
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Source system or host name.
        /// </summary>
        [JsonPropertyName("host")]
        public string Host { get; set; } = String.Empty;

        /// <summary>
        /// Program or syslog tag. Empty when unknown.
        /// </summary>
        [JsonPropertyName("program")]
        public string Program { get; set; } = String.Empty;

        /// <summary>
        /// Severity as reported by the provider. Empty when unknown.
        /// </summary>
        [JsonPropertyName("severity")]
        public string Severity { get; set; } = String.Empty;

        /// <summary>
        /// Message text, possibly shortened to the connector's message limit.
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = String.Empty;
    }
}
