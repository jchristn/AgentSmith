namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Process-wide cache of OAuth 2.0 client credentials access tokens. Entries are keyed by a SHA-256 hash of the token
    /// URL, client ID, client secret, and scopes, so plaintext credentials are never used as keys and an edited secret
    /// gets a new token. One request per key is in flight at a time. Thread-safe.
    /// </summary>
    public static class OAuth2TokenCache
    {
        #region Public-Members

        /// <summary>
        /// How long before the server's expiry a token is refreshed. Minimum 0, maximum 1 hour, default 60 seconds.
        /// </summary>
        public static TimeSpan RefreshMargin
        {
            get
            {
                return _RefreshMargin;
            }
            set
            {
                if (value < TimeSpan.Zero) value = TimeSpan.Zero;
                if (value > TimeSpan.FromHours(1)) value = TimeSpan.FromHours(1);
                _RefreshMargin = value;
            }
        }

        /// <summary>
        /// Lifetime assumed when the token response has no <c>expires_in</c>. Minimum 10 seconds, maximum 1 day, default
        /// 5 minutes.
        /// </summary>
        public static TimeSpan DefaultLifetime
        {
            get
            {
                return _DefaultLifetime;
            }
            set
            {
                if (value < TimeSpan.FromSeconds(10)) value = TimeSpan.FromSeconds(10);
                if (value > TimeSpan.FromDays(1)) value = TimeSpan.FromDays(1);
                _DefaultLifetime = value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly ConcurrentDictionary<string, OAuth2CachedToken> _Tokens = new ConcurrentDictionary<string, OAuth2CachedToken>(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _Locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private static TimeSpan _RefreshMargin = TimeSpan.FromSeconds(60);
        private static TimeSpan _DefaultLifetime = TimeSpan.FromMinutes(5);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Cache key for a token URL, credentials, and scopes: a hex SHA-256 hash.
        /// </summary>
        /// <param name="tokenUrl">Token URL.</param>
        /// <param name="credentials">Credentials.</param>
        /// <param name="scopes">Scopes.</param>
        /// <returns>Key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string KeyFor(string tokenUrl, OAuth2ClientCredentials credentials, IList<string> scopes)
        {
            ArgumentNullException.ThrowIfNull(tokenUrl);
            ArgumentNullException.ThrowIfNull(credentials);
            ArgumentNullException.ThrowIfNull(scopes);
            string material = tokenUrl + "\n" + credentials.ClientId + "\n" + credentials.ClientSecret + "\n" + String.Join(" ", scopes);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(hash);
        }

        /// <summary>
        /// Return a valid access token, requesting a new one when none is cached or the cached one is due for refresh.
        /// </summary>
        /// <param name="client">HTTP client.</param>
        /// <param name="tokenUrl">Token URL.</param>
        /// <param name="credentials">Client credentials.</param>
        /// <param name="scopes">Scopes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Access token.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>auth_failed</c> when the token endpoint rejects the client, or <c>connection_failed</c> when it cannot be reached.</exception>
        public static async Task<string> GetTokenAsync(HttpClient client, string tokenUrl, OAuth2ClientCredentials credentials, IList<string> scopes, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(tokenUrl);
            ArgumentNullException.ThrowIfNull(credentials);
            ArgumentNullException.ThrowIfNull(scopes);
            string key = KeyFor(tokenUrl, credentials, scopes);
            if (_Tokens.TryGetValue(key, out OAuth2CachedToken? cached) && cached.RefreshAfter > DateTimeOffset.UtcNow) return cached.AccessToken;

            SemaphoreSlim gate = _Locks.GetOrAdd(key, k => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (_Tokens.TryGetValue(key, out cached) && cached.RefreshAfter > DateTimeOffset.UtcNow) return cached.AccessToken;
                OAuth2CachedToken fresh = await RequestAsync(client, tokenUrl, credentials, scopes, token).ConfigureAwait(false);
                _Tokens[key] = fresh;
                return fresh.AccessToken;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Drop a cached token, for example after the API rejected it.
        /// </summary>
        /// <param name="key">Key from <see cref="KeyFor"/>.</param>
        public static void Invalidate(string key)
        {
            if (String.IsNullOrEmpty(key)) return;
            _Tokens.TryRemove(key, out OAuth2CachedToken? _);
        }

        #endregion

        #region Private-Methods

        private static async Task<OAuth2CachedToken> RequestAsync(HttpClient client, string tokenUrl, OAuth2ClientCredentials credentials, IList<string> scopes, CancellationToken token)
        {
            List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            };
            if (scopes.Count > 0) form.Add(new KeyValuePair<string, string>("scope", String.Join(" ", scopes)));

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUrl))
            {
                string basic = Uri.EscapeDataString(credentials.ClientId) + ":" + Uri.EscapeDataString(credentials.ClientSecret);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basic)));
                request.Headers.Accept.ParseAdd("application/json");
                request.Content = new FormUrlEncodedContent(form);

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request, token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The OAuth2 token endpoint could not be reached: " + e.Message, e);
                }

                using (response)
                {
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        throw new ConnectorException(ConnectorErrorCodes.AuthFailed, "The OAuth2 token endpoint rejected the client credentials (HTTP " + (int)response.StatusCode + "). Check the clientCredentials secret, tokenUrl, and scopes.");
                    if (!response.IsSuccessStatusCode)
                        throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The OAuth2 token endpoint returned HTTP " + (int)response.StatusCode + ".");

                    OAuth2TokenResponse? parsed;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<OAuth2TokenResponse>(body, ObservabilityJson.Options);
                    }
                    catch (JsonException)
                    {
                        parsed = null;
                    }
                    if (parsed == null || String.IsNullOrEmpty(parsed.AccessToken))
                        throw new ConnectorException(ConnectorErrorCodes.UpstreamError, "The OAuth2 token endpoint returned no access_token.");

                    TimeSpan lifetime = parsed.ExpiresIn.HasValue && parsed.ExpiresIn.Value > 0 ? TimeSpan.FromSeconds(parsed.ExpiresIn.Value) : _DefaultLifetime;
                    TimeSpan usable = lifetime - _RefreshMargin;
                    if (usable < TimeSpan.Zero) usable = TimeSpan.Zero;
                    return new OAuth2CachedToken(parsed.AccessToken, DateTimeOffset.UtcNow + usable);
                }
            }
        }

        #endregion
    }
}
