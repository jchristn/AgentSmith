namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Obtains and caches OAuth2 access tokens with the client credentials grant. Tokens are keyed by a SHA-256 hash of
    /// the token URL, client ID, scopes, and client secret (the plaintext secret is never used as a key), refreshed
    /// <see cref="RefreshSkew"/> before they expire, and fetched under a per-key lock so concurrent calls do not
    /// stampede the token endpoint. Thread-safe.
    /// </summary>
    public class OAuth2TokenCache
    {
        #region Public-Members

        /// <summary>
        /// Process-wide cache used by the connector.
        /// </summary>
        public static OAuth2TokenCache Shared { get; } = new OAuth2TokenCache();

        /// <summary>
        /// How long before expiry a token is refreshed. Minimum 0, maximum 1 hour, default 60 seconds.
        /// </summary>
        public TimeSpan RefreshSkew
        {
            get
            {
                return _RefreshSkew;
            }
            set
            {
                if (value < TimeSpan.Zero) value = TimeSpan.Zero;
                if (value > TimeSpan.FromHours(1)) value = TimeSpan.FromHours(1);
                _RefreshSkew = value;
            }
        }

        /// <summary>
        /// Lifetime assumed when the token endpoint sends no <c>expires_in</c>. Minimum 1 minute, maximum 1 day,
        /// default 1 hour.
        /// </summary>
        public TimeSpan DefaultLifetime
        {
            get
            {
                return _DefaultLifetime;
            }
            set
            {
                if (value < TimeSpan.FromMinutes(1)) value = TimeSpan.FromMinutes(1);
                if (value > TimeSpan.FromDays(1)) value = TimeSpan.FromDays(1);
                _DefaultLifetime = value;
            }
        }

        /// <summary>
        /// Maximum characters of a token endpoint error description included in an error. Minimum 32, maximum 2000,
        /// default 300.
        /// </summary>
        public int MaxErrorChars
        {
            get
            {
                return _MaxErrorChars;
            }
            set
            {
                _MaxErrorChars = Math.Clamp(value, 32, 2000);
            }
        }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, OAuth2CachedToken> _Tokens = new ConcurrentDictionary<string, OAuth2CachedToken>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _Locks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);
        private TimeSpan _RefreshSkew = TimeSpan.FromSeconds(60);
        private TimeSpan _DefaultLifetime = TimeSpan.FromHours(1);
        private int _MaxErrorChars = 300;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get a valid access token, fetching one when none is cached, the cached one is about to expire, or the cached
        /// one is the token the API just rejected.
        /// </summary>
        /// <param name="tokenUri">Token endpoint.</param>
        /// <param name="clientId">Client ID.</param>
        /// <param name="clientSecret">Client secret.</param>
        /// <param name="scopes">Scopes. Null means none.</param>
        /// <param name="verifyTls">Whether to verify the token endpoint's certificate.</param>
        /// <param name="rejectedAccessToken">An access token the API rejected with 401, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Token.</returns>
        /// <exception cref="ArgumentNullException">Thrown when tokenUri, clientId, or clientSecret is null or empty.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>auth_failed</c> when the endpoint refuses the client, or <c>connection_failed</c> when it is unreachable.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public async Task<OAuth2CachedToken> GetTokenAsync(Uri tokenUri, string clientId, string clientSecret, IList<string>? scopes, bool verifyTls, string? rejectedAccessToken, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(tokenUri);
            if (String.IsNullOrEmpty(clientId)) throw new ArgumentNullException(nameof(clientId));
            if (String.IsNullOrEmpty(clientSecret)) throw new ArgumentNullException(nameof(clientSecret));
            string scope = scopes == null ? String.Empty : String.Join(" ", scopes);
            string key = Key(tokenUri, clientId, clientSecret, scope);

            if (IsUsable(key, rejectedAccessToken, out OAuth2CachedToken? cached)) return cached!;
            SemaphoreSlim gate = _Locks.GetOrAdd(key, k => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (IsUsable(key, rejectedAccessToken, out cached)) return cached!;
                OAuth2CachedToken fresh = await FetchAsync(tokenUri, clientId, clientSecret, scope, verifyTls, token).ConfigureAwait(false);
                _Tokens[key] = fresh;
                return fresh;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Drop every cached token.
        /// </summary>
        public void Clear()
        {
            _Tokens.Clear();
        }

        #endregion

        #region Private-Methods

        private bool IsUsable(string key, string? rejectedAccessToken, out OAuth2CachedToken? cached)
        {
            if (_Tokens.TryGetValue(key, out cached)
                && DateTime.UtcNow + _RefreshSkew < cached.ExpiresUtc
                && !String.Equals(cached.AccessToken, rejectedAccessToken, StringComparison.Ordinal))
                return true;
            cached = null;
            return false;
        }

        private async Task<OAuth2CachedToken> FetchAsync(Uri tokenUri, string clientId, string clientSecret, string scope, bool verifyTls, CancellationToken token)
        {
            List<KeyValuePair<string, string>> form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            };
            if (!String.IsNullOrEmpty(scope)) form.Add(new KeyValuePair<string, string>("scope", scope));

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, tokenUri))
            using (FormUrlEncodedContent content = new FormUrlEncodedContent(form))
            {
                string basic = Uri.EscapeDataString(clientId) + ":" + Uri.EscapeDataString(clientSecret);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(basic)));
                request.Headers.Accept.ParseAdd("application/json");
                request.Content = content;

                HttpResponseMessage response;
                try
                {
                    response = await HttpConnectorClients.For(verifyTls).SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new ConnectorException(ConnectorErrorCodes.ConnectionFailed, "The OAuth2 token endpoint " + tokenUri.GetLeftPart(UriPartial.Path) + " could not be reached: " + e.Message, e);
                }

                using (response)
                {
                    string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        OAuth2ErrorResponse? error = TryRead<OAuth2ErrorResponse>(body);
                        string detail = error == null || String.IsNullOrEmpty(error.Error)
                            ? String.Empty
                            : " Error: " + Shorten(error.Error) + (String.IsNullOrEmpty(error.ErrorDescription) ? String.Empty : " (" + Shorten(error.ErrorDescription) + ")") + ".";
                        throw new ConnectorException(
                            ConnectorErrorCodes.AuthFailed,
                            "The OAuth2 token endpoint refused the client credentials (HTTP " + (int)response.StatusCode + ")." + detail + " Check the clientCredentials secret, tokenUrl, and scopes of this resource.");
                    }

                    OAuth2TokenResponse? parsed = TryRead<OAuth2TokenResponse>(body);
                    if (parsed == null || String.IsNullOrEmpty(parsed.AccessToken))
                        throw new ConnectorException(ConnectorErrorCodes.AuthFailed, "The OAuth2 token endpoint returned no access_token.");

                    TimeSpan lifetime = parsed.ExpiresIn.HasValue && parsed.ExpiresIn.Value > 0 ? TimeSpan.FromSeconds(parsed.ExpiresIn.Value) : _DefaultLifetime;
                    return new OAuth2CachedToken(parsed.AccessToken, parsed.TokenType, DateTime.UtcNow + lifetime);
                }
            }
        }

        private static T? TryRead<T>(string body) where T : class
        {
            if (String.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{')) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(body);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Key(Uri tokenUri, string clientId, string clientSecret, string scope)
        {
            string material = tokenUri.AbsoluteUri + "\n" + clientId + "\n" + scope + "\n" + clientSecret;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return Convert.ToHexString(hash);
        }

        private string Shorten(string value)
        {
            string v = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return v.Length > _MaxErrorChars ? v.Substring(0, _MaxErrorChars) + "..." : v;
        }

        #endregion
    }
}
