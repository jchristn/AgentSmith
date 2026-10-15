namespace Hugo.Connectors.Http
{
    /// <summary>
    /// How the <c>http</c> connector authenticates requests.
    /// </summary>
    public enum HttpAuthTypeEnum
    {
        /// <summary>No authentication.</summary>
        None = 0,

        /// <summary>HTTP Basic from the <c>basicAuth</c> slot (fields <c>username</c> and <c>password</c>).</summary>
        Basic = 1,

        /// <summary><c>Authorization: Bearer</c> from the <c>bearerToken</c> slot (field <c>token</c>).</summary>
        Bearer = 2,

        /// <summary>A custom header (<c>apiKeyHeaderName</c>) from the <c>apiKey</c> slot (field <c>token</c>).</summary>
        ApiKeyHeader = 3,

        /// <summary>OAuth2 client credentials grant against <c>tokenUrl</c> with the <c>clientCredentials</c> slot.</summary>
        OAuth2ClientCredentials = 4
    }
}
