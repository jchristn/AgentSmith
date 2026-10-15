namespace Hugo.Connectors.Observability
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Applies a resource's authentication to outgoing requests. Secrets are read from the call's
    /// <see cref="SecretAccessor"/> at the moment of use and never stored. Thread-safe.
    /// </summary>
    public static class ObservabilityAuth
    {
        #region Public-Methods

        /// <summary>
        /// Read OAuth 2.0 client credentials from the <c>clientCredentials</c> slot (Generic <c>clientId</c> and
        /// <c>clientSecret</c>, or Password <c>username</c> and <c>password</c>).
        /// </summary>
        /// <param name="secrets">Secrets.</param>
        /// <returns>Credentials, or null when incomplete.</returns>
        /// <exception cref="ArgumentNullException">Thrown when secrets is null.</exception>
        public static OAuth2ClientCredentials? ReadClientCredentials(SecretAccessor secrets)
        {
            ArgumentNullException.ThrowIfNull(secrets);
            string slot = ObservabilitySettingKeys.ClientCredentialsSlot;
            string? id = secrets.Get(slot, "clientId");
            string? secret = secrets.Get(slot, "clientSecret");
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(secret))
            {
                id = secrets.Get(slot, "username");
                secret = secrets.Get(slot, "password");
            }
            if (String.IsNullOrEmpty(id) || String.IsNullOrEmpty(secret)) return null;
            return new OAuth2ClientCredentials(id, secret);
        }

        /// <summary>
        /// Add the authentication header to a request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <param name="secrets">Secrets of the call.</param>
        /// <param name="client">HTTP client used for the OAuth 2.0 token request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The OAuth 2.0 cache key when a cached token was used, otherwise null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>secret_missing</c> when the secret the authType needs is not bound.</exception>
        public static async Task<string?> ApplyAsync(HttpRequestMessage request, ObservabilityEndpoint endpoint, SecretAccessor secrets, HttpClient client, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(secrets);
            ArgumentNullException.ThrowIfNull(client);

            switch (endpoint.AuthType)
            {
                case ObservabilityAuthTypeEnum.Basic:
                    {
                        string? user = secrets.Get(ObservabilitySettingKeys.BasicAuthSlot, "username");
                        string? password = secrets.Get(ObservabilitySettingKeys.BasicAuthSlot, "password");
                        if (String.IsNullOrEmpty(user) || password == null) throw Missing(ObservabilitySettingKeys.BasicAuthSlot, "a username and password");
                        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
                        return null;
                    }
                case ObservabilityAuthTypeEnum.Bearer:
                    {
                        string? bearer = secrets.Get(ObservabilitySettingKeys.BearerTokenSlot, "token");
                        if (String.IsNullOrEmpty(bearer)) throw Missing(ObservabilitySettingKeys.BearerTokenSlot, "a token");
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                        return null;
                    }
                case ObservabilityAuthTypeEnum.ApiKeyHeader:
                    {
                        string? key = secrets.Get(ObservabilitySettingKeys.ApiKeySlot, "token");
                        if (String.IsNullOrEmpty(key)) throw Missing(ObservabilitySettingKeys.ApiKeySlot, "a key");
                        request.Headers.Remove(endpoint.ApiKeyHeaderName);
                        request.Headers.TryAddWithoutValidation(endpoint.ApiKeyHeaderName, endpoint.ApiKeyPrefix + key);
                        return null;
                    }
                case ObservabilityAuthTypeEnum.OAuth2ClientCredentials:
                    {
                        OAuth2ClientCredentials? credentials = ReadClientCredentials(secrets);
                        if (credentials == null) throw Missing(ObservabilitySettingKeys.ClientCredentialsSlot, "a client ID and client secret");
                        if (String.IsNullOrEmpty(endpoint.TokenUrl))
                            throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "authType is OAuth2ClientCredentials but tokenUrl is not set.");
                        string access = await OAuth2TokenCache.GetTokenAsync(client, endpoint.TokenUrl, credentials, endpoint.Scopes, token).ConfigureAwait(false);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
                        return OAuth2TokenCache.KeyFor(endpoint.TokenUrl, credentials, endpoint.Scopes);
                    }
                default:
                    return null;
            }
        }

        #endregion

        #region Private-Methods

        private static ConnectorException Missing(string slot, string what)
        {
            return new ConnectorException(ConnectorErrorCodes.SecretMissing, "The authType of this resource needs " + what + " in the '" + slot + "' secret slot, but none is bound.");
        }

        #endregion
    }
}
