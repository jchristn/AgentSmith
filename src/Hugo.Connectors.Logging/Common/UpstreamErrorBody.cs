namespace Hugo.Connectors.Logging
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Error body returned by SolarWinds Observability
    /// (<c>{"message": "...", "code": "...", "target": "..."}</c>).
    /// </summary>
    public class UpstreamErrorBody
    {
        /// <summary>
        /// Error message. May be null.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; } = null;

        /// <summary>
        /// Error code (SolarWinds Observability only, for example <c>Unauthorized</c>). May be null.
        /// </summary>
        [JsonPropertyName("code")]
        public string? Code { get; set; } = null;

        /// <summary>
        /// Invalid field (SolarWinds Observability 400 and 409 only). May be null.
        /// </summary>
        [JsonPropertyName("target")]
        public string? Target { get; set; } = null;
    }
}
