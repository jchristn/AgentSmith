namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// Setting keys and secret slot names shared by the observability connectors. These are part of the resource
    /// contract and are not configurable. Thread-safe.
    /// </summary>
    public static class ObservabilitySettingKeys
    {
        /// <summary>Base URL of the backend API.</summary>
        public const string BaseUrl = "baseUrl";

        /// <summary>Authentication type (<see cref="ObservabilityAuthTypeEnum"/>).</summary>
        public const string AuthType = "authType";

        /// <summary>Header name used by ApiKeyHeader authentication.</summary>
        public const string ApiKeyHeaderName = "apiKeyHeaderName";

        /// <summary>Optional prefix placed before the API key value (for example <c>ApiKey </c>).</summary>
        public const string ApiKeyPrefix = "apiKeyPrefix";

        /// <summary>OAuth 2.0 token endpoint.</summary>
        public const string TokenUrl = "tokenUrl";

        /// <summary>OAuth 2.0 scopes, one per line.</summary>
        public const string Scopes = "scopes";

        /// <summary>Headers sent with every request, one <c>Name: value</c> per line.</summary>
        public const string DefaultHeaders = "defaultHeaders";

        /// <summary>Whether TLS certificates are verified.</summary>
        public const string VerifyTls = "verifyTls";

        /// <summary>Hard cap on response bytes read from the backend.</summary>
        public const string MaxResponseBytes = "maxResponseBytes";

        /// <summary>Longest time range one query may cover, in hours (Prometheus and Loki).</summary>
        public const string MaxRangeHours = "maxRangeHours";

        /// <summary>Maximum series returned by a Prometheus call.</summary>
        public const string MaxSeries = "maxSeries";

        /// <summary>Maximum points per series in a Prometheus range query.</summary>
        public const string MaxPointsPerSeries = "maxPointsPerSeries";

        /// <summary>Loki tenant ID sent as <c>X-Scope-OrgID</c>.</summary>
        public const string TenantHeader = "tenantHeader";

        /// <summary>Maximum log lines returned by a Loki call.</summary>
        public const string MaxLines = "maxLines";

        /// <summary>Elasticsearch flavor (<see cref="ElasticsearchFlavorEnum"/>).</summary>
        public const string Flavor = "flavor";

        /// <summary>Maximum hits returned by one Elasticsearch search.</summary>
        public const string MaxHits = "maxHits";

        /// <summary>Index patterns Elasticsearch calls may address, one glob per line.</summary>
        public const string AllowedIndexPatterns = "allowedIndexPatterns";

        /// <summary>Basic authentication slot (Password secret: <c>username</c>, <c>password</c>).</summary>
        public const string BasicAuthSlot = "basicAuth";

        /// <summary>Bearer token slot (ApiToken secret: <c>token</c>).</summary>
        public const string BearerTokenSlot = "bearerToken";

        /// <summary>API key slot (ApiToken secret: <c>token</c>).</summary>
        public const string ApiKeySlot = "apiKey";

        /// <summary>OAuth 2.0 client credentials slot (Generic secret: <c>clientId</c> and <c>clientSecret</c>, or Password: <c>username</c> and <c>password</c>).</summary>
        public const string ClientCredentialsSlot = "clientCredentials";
    }
}
