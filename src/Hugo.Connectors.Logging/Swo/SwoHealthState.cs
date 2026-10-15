namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Health state of a SolarWinds Observability entity (<c>Entities.HealthState</c>).
    /// </summary>
    public class SwoHealthState
    {
        /// <summary>
        /// One of <c>GOOD</c>, <c>MODERATE</c>, <c>AT_RISK</c>, <c>DEGRADED</c>, <c>BAD</c>, <c>UNKNOWN</c>, <c>DISABLED</c>.
        /// </summary>
        [JsonPropertyName("state")]
        public string? State { get; set; } = null;
    }
}
