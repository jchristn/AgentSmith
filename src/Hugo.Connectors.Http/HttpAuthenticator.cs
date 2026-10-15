namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Applies a resource's authentication to outgoing requests for the duration of one call. Secrets are read from the
    /// resource's secret accessor when the instance is built and are never logged or returned. Not thread-safe; use one
    /// instance per call.
    /// </summary>
    public class HttpAuthenticator
    {
        #region Public-Members

        /// <summary>
        /// Authentication type.
        /// </summary>
        public HttpAuthTypeEnum AuthType { get; }

        /// <summary>
        /// Whether a rejected token can be refreshed (OAuth2 only).
        /// </summary>
        public bool CanRefresh
        {
            get
            {
                return AuthType == HttpAuthTypeEnum.OAuth2ClientCredentials;
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpResourceOptions _Options;
        private readonly OAuth2TokenCache _Cache;
        private readonly string? _Username;
        private readonly string? _Password;
        private readonly string? _Token;
        private readonly string? _ClientId;
        private readonly string? _ClientSecret;
        private string? _LastAccessToken = null;
        private string? _RejectedAccessToken = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and read the secrets the authentication type needs.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="options">Parsed options.</param>
        /// <param name="cache">Token cache. Null uses <see cref="OAuth2TokenCache.Shared"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when resource or options is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>secret_missing</c> when a required secret is not bound, or <c>invalid_setting</c> when OAuth2 has no tokenUrl.</exception>
        public HttpAuthenticator(ConnectorResourceContext resource, HttpResourceOptions options, OAuth2TokenCache? cache = null)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(options);
            _Options = options;
            _Cache = cache ?? OAuth2TokenCache.Shared;
            AuthType = options.AuthType;
            SecretAccessor s = resource.Secrets;

            switch (AuthType)
            {
                case HttpAuthTypeEnum.Basic:
                    _Username = s.Get(HttpSettingKeys.BasicAuthSlot, HttpSettingKeys.UsernameField);
                    _Password = s.Get(HttpSettingKeys.BasicAuthSlot, HttpSettingKeys.PasswordField);
                    if (String.IsNullOrEmpty(_Username) || _Password == null)
                        throw Missing(resource, HttpSettingKeys.BasicAuthSlot, "a Password secret with a username and password");
                    break;
                case HttpAuthTypeEnum.Bearer:
                    _Token = s.Get(HttpSettingKeys.BearerTokenSlot, HttpSettingKeys.TokenField);
                    if (String.IsNullOrEmpty(_Token)) throw Missing(resource, HttpSettingKeys.BearerTokenSlot, "an ApiToken secret");
                    break;
                case HttpAuthTypeEnum.ApiKeyHeader:
                    _Token = s.Get(HttpSettingKeys.ApiKeySlot, HttpSettingKeys.TokenField);
                    if (String.IsNullOrEmpty(_Token)) throw Missing(resource, HttpSettingKeys.ApiKeySlot, "an ApiToken secret");
                    break;
                case HttpAuthTypeEnum.OAuth2ClientCredentials:
                    _ClientId = s.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.ClientIdField) ?? s.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.UsernameField);
                    _ClientSecret = s.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.ClientSecretField) ?? s.Get(HttpSettingKeys.ClientCredentialsSlot, HttpSettingKeys.PasswordField);
                    if (String.IsNullOrEmpty(_ClientId) || String.IsNullOrEmpty(_ClientSecret))
                        throw Missing(resource, HttpSettingKeys.ClientCredentialsSlot, "a Generic secret with clientId and clientSecret (or a Password secret with username and password)");
                    if (options.TokenUri == null)
                        throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "authType is OAuth2ClientCredentials but tokenUrl is missing or not an absolute http or https URL.");
                    break;
                default:
                    break;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Header names the caller may not set because authentication owns them (case-insensitive).
        /// </summary>
        /// <param name="options">Options.</param>
        /// <returns>Names.</returns>
        public static HashSet<string> ReservedHeaderNames(HttpResourceOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Authorization", "Proxy-Authorization", "Cookie" };
            if (!String.IsNullOrEmpty(options.ApiKeyHeaderName)) names.Add(options.ApiKeyHeaderName);
            return names;
        }

        /// <summary>
        /// Add the authentication header to a request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ConnectorException">Thrown when an OAuth2 token cannot be obtained.</exception>
        public async Task ApplyAsync(HttpRequestMessage request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            switch (AuthType)
            {
                case HttpAuthTypeEnum.Basic:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(_Username + ":" + _Password)));
                    break;
                case HttpAuthTypeEnum.Bearer:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _Token);
                    break;
                case HttpAuthTypeEnum.ApiKeyHeader:
                    request.Headers.Remove(_Options.ApiKeyHeaderName);
                    request.Headers.TryAddWithoutValidation(_Options.ApiKeyHeaderName, _Token);
                    break;
                case HttpAuthTypeEnum.OAuth2ClientCredentials:
                    OAuth2CachedToken access = await _Cache.GetTokenAsync(_Options.TokenUri!, _ClientId!, _ClientSecret!, _Options.Scopes, _Options.VerifyTls, _RejectedAccessToken, token).ConfigureAwait(false);
                    _LastAccessToken = access.AccessToken;
                    request.Headers.Authorization = new AuthenticationHeaderValue(access.TokenType, access.AccessToken);
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// Mark the last OAuth2 access token as rejected, so the next <see cref="ApplyAsync"/> fetches a new one.
        /// </summary>
        public void MarkRejected()
        {
            _RejectedAccessToken = _LastAccessToken;
        }

        #endregion

        #region Private-Methods

        private static ConnectorException Missing(ConnectorResourceContext resource, string slot, string what)
        {
            return new ConnectorException(ConnectorErrorCodes.SecretMissing, "Resource '" + resource.Name + "' uses this authentication type but no " + what + " is bound to the '" + slot + "' slot.");
        }

        #endregion
    }
}
