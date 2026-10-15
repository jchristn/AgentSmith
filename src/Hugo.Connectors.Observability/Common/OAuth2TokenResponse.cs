namespace Hugo.Connectors.Observability
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Token endpoint response of the OAuth 2.0 client credentials grant (RFC 6749 section 5.1).
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
        /// Lifetime in seconds, when the server reports it.
        /// </summary>
        [JsonPropertyName("expires_in")]
        public double? ExpiresIn { get; set; } = null;
    }
}
