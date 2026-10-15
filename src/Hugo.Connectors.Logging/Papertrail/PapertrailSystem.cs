namespace Hugo.Connectors.Logging
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A Papertrail system (sender), from <c>GET /systems.json</c>.
    /// </summary>
    public class PapertrailSystem
    {
        /// <summary>
        /// System ID.
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; } = 0;

        /// <summary>
        /// System name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; } = null;

        /// <summary>
        /// Time of the last event received, or null.
        /// </summary>
        [JsonPropertyName("last_event_at")]
        public DateTimeOffset? LastEventAt { get; set; } = null;

        /// <summary>
        /// IP address, or null.
        /// </summary>
        [JsonPropertyName("ip_address")]
        public string? IpAddress { get; set; } = null;

        /// <summary>
        /// Hostname filter, or null.
        /// </summary>
        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// Syslog destination, or null.
        /// </summary>
        [JsonPropertyName("syslog")]
        public PapertrailSyslogEndpoint? Syslog { get; set; } = null;
    }
}
