namespace Hugo.Connectors.Http
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Successful OAuth2 token endpoint response (RFC 6749 section 5.1).
    /// </summary>
    public class OAuth2TokenResponse
    {
        /// <summary>
        /// Access token.
        /// </summary>
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; } = null;

        /// <summary>
        /// Token type, normally <c>Bearer</c>.
        /// </summary>
        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; } = null;

        /// <summary>
        /// Lifetime in seconds, or null when the server did not say.
        /// </summary>
        [JsonPropertyName("expires_in")]
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public long? ExpiresIn { get; set; } = null;
    }
}
