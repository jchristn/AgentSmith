namespace Hugo.Connectors.Http
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// OAuth2 token endpoint error response (RFC 6749 section 5.2).
    /// </summary>
    public class OAuth2ErrorResponse
    {
        /// <summary>
        /// Error code, for example <c>invalid_client</c>.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; } = null;

        /// <summary>
        /// Human-readable description.
        /// </summary>
        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; } = null;
    }
}
