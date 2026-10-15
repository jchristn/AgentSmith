namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Connection settings of one observability resource, parsed from its settings. Credentials are not held here;
    /// <see cref="ObservabilityAuth"/> reads them from the resource's secrets per request. Immutable after construction
    /// and thread-safe.
    /// </summary>
    public class ObservabilityEndpoint
    {
        #region Public-Members

        /// <summary>
        /// Absolute base URL without a trailing slash.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Authentication type. Default <see cref="ObservabilityAuthTypeEnum.None"/>.
        /// </summary>
        public ObservabilityAuthTypeEnum AuthType { get; }

        /// <summary>
        /// Header carrying the API key for <see cref="ObservabilityAuthTypeEnum.ApiKeyHeader"/>. Default <c>X-API-Key</c>.
        /// </summary>
        public string ApiKeyHeaderName { get; }

        /// <summary>
        /// Text placed before the API key value (for example <c>ApiKey </c>). Default empty.
        /// </summary>
        public string ApiKeyPrefix { get; }

        /// <summary>
        /// OAuth 2.0 token endpoint, or null.
        /// </summary>
        public string? TokenUrl { get; }

        /// <summary>
        /// OAuth 2.0 scopes. Never null.
        /// </summary>
        public List<string> Scopes { get; }

        /// <summary>
        /// Headers sent with every request. Never null.
        /// </summary>
        public Dictionary<string, string> DefaultHeaders { get; }

        /// <summary>
        /// Whether TLS certificates are verified. Default true.
        /// </summary>
        public bool VerifyTls { get; }

        /// <summary>
        /// Most response bytes read from one call. Minimum 65536, maximum 268435456, default 8388608 (8 MiB).
        /// </summary>
        public int MaxResponseBytes { get; }

        /// <summary>
        /// Default API key header name.
        /// </summary>
        public static readonly string DefaultApiKeyHeaderName = "X-API-Key";

        /// <summary>
        /// Default response byte cap.
        /// </summary>
        public static readonly int DefaultMaxResponseBytes = 8 * 1024 * 1024;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse the endpoint from resource settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ConnectorException">Thrown with code <c>invalid_setting</c> when baseUrl or authType is invalid.</exception>
        public ObservabilityEndpoint(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            string? baseUrl = settings.GetString(ObservabilitySettingKeys.BaseUrl);
            if (!ObservabilitySettingsSupport.IsHttpUrl(baseUrl))
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "Setting 'baseUrl' must be an absolute http or https URL.");
            BaseUrl = baseUrl!.Trim().TrimEnd('/');

            string authText = settings.GetString(ObservabilitySettingKeys.AuthType, "None") ?? "None";
            if (!Enum.TryParse(authText, true, out ObservabilityAuthTypeEnum auth) || !Enum.IsDefined(typeof(ObservabilityAuthTypeEnum), auth))
                throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "Setting 'authType' must be None, Basic, Bearer, ApiKeyHeader, or OAuth2ClientCredentials.");
            AuthType = auth;

            ApiKeyHeaderName = settings.GetString(ObservabilitySettingKeys.ApiKeyHeaderName, DefaultApiKeyHeaderName) ?? DefaultApiKeyHeaderName;
            ApiKeyPrefix = settings.GetString(ObservabilitySettingKeys.ApiKeyPrefix, String.Empty) ?? String.Empty;
            TokenUrl = settings.GetString(ObservabilitySettingKeys.TokenUrl);
            Scopes = settings.GetStringList(ObservabilitySettingKeys.Scopes);
            DefaultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in settings.GetStringList(ObservabilitySettingKeys.DefaultHeaders))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                DefaultHeaders[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }
            VerifyTls = settings.GetBool(ObservabilitySettingKeys.VerifyTls, true);
            MaxResponseBytes = settings.GetInt(ObservabilitySettingKeys.MaxResponseBytes, DefaultMaxResponseBytes, 65536, 256 * 1024 * 1024);
        }

        #endregion
    }
}
