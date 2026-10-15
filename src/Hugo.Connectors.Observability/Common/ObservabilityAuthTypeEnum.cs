namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// How an observability connector authenticates to its backend.
    /// </summary>
    public enum ObservabilityAuthTypeEnum
    {
        /// <summary>
        /// No authentication header is sent.
        /// </summary>
        None = 0,

        /// <summary>
        /// HTTP Basic with the <c>basicAuth</c> slot (fields <c>username</c> and <c>password</c>).
        /// </summary>
        Basic = 1,

        /// <summary>
        /// <c>Authorization: Bearer</c> with the <c>bearerToken</c> slot (field <c>token</c>).
        /// </summary>
        Bearer = 2,

        /// <summary>
        /// A custom header (<c>apiKeyHeaderName</c>, optional <c>apiKeyPrefix</c>) with the <c>apiKey</c> slot.
        /// </summary>
        ApiKeyHeader = 3,

        /// <summary>
        /// OAuth 2.0 client credentials grant against <c>tokenUrl</c> with the <c>clientCredentials</c> slot; the access
        /// token is cached until shortly before it expires.
        /// </summary>
        OAuth2ClientCredentials = 4
    }
}
