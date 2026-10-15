namespace Hugo.Connectors.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Settings, secret slots, and validation shared by the Prometheus, Loki, and Elasticsearch connectors. Thread-safe.
    /// </summary>
    public static class ObservabilitySettingsSupport
    {
        #region Private-Members

        private static readonly Regex _HeaderName = new Regex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a value is an absolute http or https URL.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsHttpUrl(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)) return false;
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        /// <summary>
        /// Whether a value is a valid HTTP header name (RFC 9110 token).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>True when valid.</returns>
        public static bool IsHeaderName(string? value)
        {
            return !String.IsNullOrEmpty(value) && _HeaderName.IsMatch(value);
        }

        /// <summary>
        /// Add the connection and authentication settings every observability connector shares.
        /// </summary>
        /// <param name="schema">Schema.</param>
        /// <param name="baseUrlHelp">Help text for baseUrl.</param>
        /// <exception cref="ArgumentNullException">Thrown when schema is null.</exception>
        public static void AddCommonSettings(ConnectorSettingsSchema schema, string baseUrlHelp)
        {
            ArgumentNullException.ThrowIfNull(schema);
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.BaseUrl, SettingValueKindEnum.String, "Base URL")
            {
                Required = true,
                Pattern = "^https?://",
                Group = "Connection",
                ShowInSummary = true,
                Help = baseUrlHelp ?? String.Empty
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.AuthType, SettingValueKindEnum.Enum, "Authentication")
            {
                DefaultValue = "None",
                AllowedValues = new List<string> { "None", "Basic", "Bearer", "ApiKeyHeader", "OAuth2ClientCredentials" },
                Group = "Authentication",
                Help = "None sends no credentials. Basic uses the basicAuth slot, Bearer the bearerToken slot, ApiKeyHeader the apiKey slot in the apiKeyHeaderName header, and OAuth2ClientCredentials exchanges the clientCredentials slot for a cached access token at tokenUrl."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.ApiKeyHeaderName, SettingValueKindEnum.String, "API key header")
            {
                DefaultValue = ObservabilityEndpoint.DefaultApiKeyHeaderName,
                Pattern = "^[!#$%&'*+.^_`|~0-9A-Za-z-]+$",
                Group = "Authentication",
                Help = "Header that carries the API key when authType is ApiKeyHeader. Default X-API-Key. Use Authorization with apiKeyPrefix 'ApiKey ' for Elasticsearch API keys."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.ApiKeyPrefix, SettingValueKindEnum.String, "API key prefix")
            {
                Group = "Authentication",
                Help = "Text placed before the API key value, including any trailing space (for example 'ApiKey '). Default empty."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.TokenUrl, SettingValueKindEnum.String, "OAuth2 token URL")
            {
                Pattern = "^https?://",
                Group = "Authentication",
                Help = "Token endpoint for OAuth2ClientCredentials. The client ID and secret are sent with HTTP Basic authentication."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.Scopes, SettingValueKindEnum.StringList, "OAuth2 scopes")
            {
                Group = "Authentication",
                Help = "Scopes requested with the client credentials grant, one per line. Empty requests none."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.DefaultHeaders, SettingValueKindEnum.StringList, "Default headers")
            {
                Group = "Connection",
                Help = "Headers sent with every request, one 'Name: value' per line. Do not put credentials here; bind a secret instead."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.VerifyTls, SettingValueKindEnum.Boolean, "Verify TLS")
            {
                DefaultValue = "true",
                Group = "Connection",
                Help = "When false, invalid or self-signed TLS certificates are accepted. Only disable for test systems."
            });
            schema.Add(new SettingDescriptor(ObservabilitySettingKeys.MaxResponseBytes, SettingValueKindEnum.Integer, "Max response bytes")
            {
                DefaultValue = ObservabilityEndpoint.DefaultMaxResponseBytes.ToString(CultureInfo.InvariantCulture),
                Minimum = 65536,
                Maximum = 256 * 1024 * 1024,
                Group = "Limits",
                Help = "Largest backend response read for one call. Larger responses fail with too_large. Default 8388608 (8 MiB)."
            });
        }

        /// <summary>
        /// Build the secret slots shared by the observability connectors. None is required by itself; the authType
        /// decides which one must be bound.
        /// </summary>
        /// <returns>Slots.</returns>
        public static List<SecretSlotDescriptor> SecretSlots()
        {
            return new List<SecretSlotDescriptor>
            {
                new SecretSlotDescriptor(ObservabilitySettingKeys.BasicAuthSlot, "Basic authentication", SecretKindEnum.Password) { Help = "Password secret with 'username' and 'password'. Used when authType is Basic." },
                new SecretSlotDescriptor(ObservabilitySettingKeys.BearerTokenSlot, "Bearer token", SecretKindEnum.BearerToken, SecretKindEnum.ApiToken) { Help = "BearerToken (or ApiToken) secret sent as 'Authorization: Bearer'. Used when authType is Bearer." },
                new SecretSlotDescriptor(ObservabilitySettingKeys.ApiKeySlot, "API key", SecretKindEnum.ApiToken) { Help = "Key sent in the apiKeyHeaderName header. Used when authType is ApiKeyHeader." },
                new SecretSlotDescriptor(ObservabilitySettingKeys.ClientCredentialsSlot, "OAuth2 client credentials", SecretKindEnum.Generic, SecretKindEnum.Password) { Help = "Generic secret with 'clientId' and 'clientSecret', or a Password secret with 'username' (client ID) and 'password' (client secret). Used when authType is OAuth2ClientCredentials." }
            };
        }

        /// <summary>
        /// Validate the shared settings and the secret the authType needs.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="result">Result to add errors to.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void ValidateCommon(ConnectorResourceContext resource, ConnectorValidationResult result)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(result);
            ResourceSettings s = resource.Settings;

            if (!IsHttpUrl(s.GetString(ObservabilitySettingKeys.BaseUrl)))
                result.Add(ObservabilitySettingKeys.BaseUrl, "invalid_format", "baseUrl must be an absolute http or https URL.");

            string authText = s.GetString(ObservabilitySettingKeys.AuthType, "None") ?? "None";
            if (!Enum.TryParse(authText, true, out ObservabilityAuthTypeEnum auth) || !Enum.IsDefined(typeof(ObservabilityAuthTypeEnum), auth))
            {
                result.Add(ObservabilitySettingKeys.AuthType, "invalid_value", "authType must be None, Basic, Bearer, ApiKeyHeader, or OAuth2ClientCredentials.");
            }
            else
            {
                SecretAccessor secrets = resource.Secrets;
                switch (auth)
                {
                    case ObservabilityAuthTypeEnum.Basic:
                        if (String.IsNullOrEmpty(secrets.Get(ObservabilitySettingKeys.BasicAuthSlot, "username")) || String.IsNullOrEmpty(secrets.Get(ObservabilitySettingKeys.BasicAuthSlot, "password")))
                            result.Add(ObservabilitySettingKeys.BasicAuthSlot, "required", "authType is Basic, so bind a Password secret with a username and password to the basicAuth slot.");
                        break;
                    case ObservabilityAuthTypeEnum.Bearer:
                        if (String.IsNullOrEmpty(secrets.Get(ObservabilitySettingKeys.BearerTokenSlot, "token")))
                            result.Add(ObservabilitySettingKeys.BearerTokenSlot, "required", "authType is Bearer, so bind a BearerToken (or ApiToken) secret to the bearerToken slot.");
                        break;
                    case ObservabilityAuthTypeEnum.ApiKeyHeader:
                        if (String.IsNullOrEmpty(secrets.Get(ObservabilitySettingKeys.ApiKeySlot, "token")))
                            result.Add(ObservabilitySettingKeys.ApiKeySlot, "required", "authType is ApiKeyHeader, so bind an ApiToken secret to the apiKey slot.");
                        break;
                    case ObservabilityAuthTypeEnum.OAuth2ClientCredentials:
                        if (!IsHttpUrl(s.GetString(ObservabilitySettingKeys.TokenUrl)))
                            result.Add(ObservabilitySettingKeys.TokenUrl, "required", "authType is OAuth2ClientCredentials, so tokenUrl must be an absolute http or https URL.");
                        if (ObservabilityAuth.ReadClientCredentials(secrets) == null)
                            result.Add(ObservabilitySettingKeys.ClientCredentialsSlot, "required", "authType is OAuth2ClientCredentials, so bind a secret with clientId and clientSecret to the clientCredentials slot.");
                        break;
                }
            }

            string? headerName = s.GetString(ObservabilitySettingKeys.ApiKeyHeaderName);
            if (headerName != null && !IsHeaderName(headerName))
                result.Add(ObservabilitySettingKeys.ApiKeyHeaderName, "invalid_format", "apiKeyHeaderName must be a valid HTTP header name.");

            foreach (string line in s.GetStringList(ObservabilitySettingKeys.DefaultHeaders))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || !IsHeaderName(line.Substring(0, colon).Trim()))
                {
                    result.Add(ObservabilitySettingKeys.DefaultHeaders, "invalid_format", "Each default header must look like 'Name: value' with a valid header name.");
                    break;
                }
            }

            ValidateRange(resource, result, ObservabilitySettingKeys.MaxResponseBytes, 65536, 256 * 1024 * 1024);
        }

        /// <summary>
        /// Add an error when an integer setting is present but outside a range.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <param name="result">Result.</param>
        /// <param name="key">Setting key.</param>
        /// <param name="min">Minimum.</param>
        /// <param name="max">Maximum.</param>
        public static void ValidateRange(ConnectorResourceContext resource, ConnectorValidationResult result, string key, long min, long max)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(result);
            string? raw = resource.Settings.GetString(key);
            if (raw == null) return;
            if (!Int64.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) || value < min || value > max)
                result.Add(key, "out_of_range", key + " must be an integer from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".");
        }

        #endregion
    }
}
