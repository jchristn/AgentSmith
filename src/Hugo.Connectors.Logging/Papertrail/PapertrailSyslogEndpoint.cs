namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Syslog destination of a Papertrail system (<c>syslog</c> object).
    /// </summary>
    public class PapertrailSyslogEndpoint
    {
        /// <summary>
        /// Destination host name.
        /// </summary>
        [JsonPropertyName("hostname")]
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// Destination port.
        /// </summary>
        [JsonPropertyName("port")]
        public int Port { get; set; } = 0;
    }
}
