namespace Hugo.Connectors.Http
{
    /// <summary>
    /// Setting keys, secret slot names, and secret field names of the <c>http</c> connector. These are part of the
    /// resource contract and are not configurable. Thread-safe.
    /// </summary>
    public static class HttpSettingKeys
    {
        /// <summary>Absolute base URL every request path is relative to.</summary>
        public const string BaseUrl = "baseUrl";

        /// <summary>Headers sent with every request, one <c>Name: value</c> per line.</summary>
        public const string DefaultHeaders = "defaultHeaders";

        /// <summary>Authentication type (see <see cref="HttpAuthTypeEnum"/>).</summary>
        public const string AuthType = "authType";

        /// <summary>Header that carries the API key when the authentication type is ApiKeyHeader.</summary>
        public const string ApiKeyHeaderName = "apiKeyHeaderName";

        /// <summary>OAuth2 token endpoint for the client credentials grant.</summary>
        public const string TokenUrl = "tokenUrl";

        /// <summary>OAuth2 scopes requested with the client credentials grant.</summary>
        public const string Scopes = "scopes";

        /// <summary>HTTP methods http_request may use.</summary>
        public const string AllowedMethods = "allowedMethods";

        /// <summary>Path prefixes, relative to the base URL path, that requests may address.</summary>
        public const string AllowedPathPrefixes = "allowedPathPrefixes";

        /// <summary>Effect overrides, one <c>METHOD /prefix=Effect</c> per line.</summary>
        public const string MethodEffectOverrides = "methodEffectOverrides";

        /// <summary>Request timeout in seconds.</summary>
        public const string TimeoutSeconds = "timeoutSeconds";

        /// <summary>Most response body bytes read per request.</summary>
        public const string MaxResponseBytes = "maxResponseBytes";

        /// <summary>Largest request body accepted from a call.</summary>
        public const string MaxRequestBytes = "maxRequestBytes";

        /// <summary>Whether redirects are followed (within policy).</summary>
        public const string FollowRedirects = "followRedirects";

        /// <summary>Most redirects followed per request.</summary>
        public const string MaxRedirects = "maxRedirects";

        /// <summary>Whether TLS certificates are verified.</summary>
        public const string VerifyTls = "verifyTls";

        /// <summary>Secret slot for HTTP Basic credentials.</summary>
        public const string BasicAuthSlot = "basicAuth";

        /// <summary>Secret slot for a bearer token.</summary>
        public const string BearerTokenSlot = "bearerToken";

        /// <summary>Secret slot for an API key.</summary>
        public const string ApiKeySlot = "apiKey";

        /// <summary>Secret slot for OAuth2 client credentials.</summary>
        public const string ClientCredentialsSlot = "clientCredentials";

        /// <summary>Secret field holding a user name (Password secrets).</summary>
        public const string UsernameField = "username";

        /// <summary>Secret field holding a password (Password secrets).</summary>
        public const string PasswordField = "password";

        /// <summary>Secret field holding a token (ApiToken secrets).</summary>
        public const string TokenField = "token";

        /// <summary>Secret field holding an OAuth2 client ID (Generic secrets).</summary>
        public const string ClientIdField = "clientId";

        /// <summary>Secret field holding an OAuth2 client secret (Generic secrets).</summary>
        public const string ClientSecretField = "clientSecret";
    }
}
