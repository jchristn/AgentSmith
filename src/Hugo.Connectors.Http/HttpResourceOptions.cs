namespace Hugo.Connectors.Http
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// The settings of one <c>http</c> resource, parsed with defaults and clamped to their ranges. Parsing never throws;
    /// invalid entries are skipped here and reported by <see cref="HttpConnector.ValidateAsync"/>. Not thread-safe for
    /// mutation; treat as immutable after <see cref="FromSettings"/>.
    /// </summary>
    public class HttpResourceOptions
    {
        #region Public-Members

        /// <summary>
        /// Default allowed methods.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultMethods = new List<string> { "GET", "HEAD", "OPTIONS", "POST", "PUT", "PATCH", "DELETE" };

        /// <summary>
        /// Base URL, or null when the setting is missing or not an absolute http or https URL.
        /// </summary>
        public Uri? BaseUri { get; set; } = null;

        /// <summary>
        /// Encoded base path without a trailing slash (empty for the root).
        /// </summary>
        public string BasePath { get; set; } = String.Empty;

        /// <summary>
        /// Headers sent with every request. Never null.
        /// </summary>
        public List<KeyValuePair<string, string>> DefaultHeaders { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// Authentication type. Default None.
        /// </summary>
        public HttpAuthTypeEnum AuthType { get; set; } = HttpAuthTypeEnum.None;

        /// <summary>
        /// API key header name. Default <c>X-API-Key</c>.
        /// </summary>
        public string ApiKeyHeaderName { get; set; } = "X-API-Key";

        /// <summary>
        /// OAuth2 token endpoint, or null.
        /// </summary>
        public Uri? TokenUri { get; set; } = null;

        /// <summary>
        /// OAuth2 scopes. Never null.
        /// </summary>
        public List<string> Scopes { get; set; } = new List<string>();

        /// <summary>
        /// Upper-case allowed methods. Never null.
        /// </summary>
        public HashSet<string> AllowedMethods { get; set; } = new HashSet<string>(DefaultMethods, StringComparer.Ordinal);

        /// <summary>
        /// Normalized allowed path prefixes. Never null; default <c>/</c>.
        /// </summary>
        public List<string> AllowedPathPrefixes { get; set; } = new List<string> { "/" };

        /// <summary>
        /// Effect overrides. Never null.
        /// </summary>
        public List<HttpEffectOverride> Overrides { get; set; } = new List<HttpEffectOverride>();

        /// <summary>
        /// Request timeout. Minimum 1 second, maximum 600 seconds, default 30 seconds.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Most response body bytes read. Minimum 1024, maximum 64 MiB, default 1 MiB.
        /// </summary>
        public int MaxResponseBytes { get; set; } = 1048576;

        /// <summary>
        /// Largest request body. Minimum 1, maximum 64 MiB, default 1 MiB.
        /// </summary>
        public int MaxRequestBytes { get; set; } = 1048576;

        /// <summary>
        /// Whether redirects are followed. Default true.
        /// </summary>
        public bool FollowRedirects { get; set; } = true;

        /// <summary>
        /// Most redirects followed. Minimum 0, maximum 20, default 5.
        /// </summary>
        public int MaxRedirects { get; set; } = 5;

        /// <summary>
        /// Whether TLS certificates are verified. Default true.
        /// </summary>
        public bool VerifyTls { get; set; } = true;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Options.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static HttpResourceOptions FromSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            HttpResourceOptions o = new HttpResourceOptions();

            if (TryParseBaseUrl(settings.GetString(HttpSettingKeys.BaseUrl), out Uri? baseUri, out string _))
            {
                o.BaseUri = baseUri;
                o.BasePath = HttpPathGuard.NormalizeBasePath(baseUri!.AbsolutePath);
            }

            foreach (string line in settings.GetStringList(HttpSettingKeys.DefaultHeaders))
            {
                if (TryParseHeaderLine(line, out string name, out string value, out string _)) o.DefaultHeaders.Add(new KeyValuePair<string, string>(name, value));
            }

            if (Enum.TryParse<HttpAuthTypeEnum>(settings.GetString(HttpSettingKeys.AuthType, "None"), true, out HttpAuthTypeEnum auth) && Enum.IsDefined(typeof(HttpAuthTypeEnum), auth))
                o.AuthType = auth;

            string apiKeyHeader = (settings.GetString(HttpSettingKeys.ApiKeyHeaderName, "X-API-Key") ?? "X-API-Key").Trim();
            if (HttpRequestClassifier.IsToken(apiKeyHeader)) o.ApiKeyHeaderName = apiKeyHeader;

            string? tokenUrl = settings.GetString(HttpSettingKeys.TokenUrl);
            if (tokenUrl != null && Uri.TryCreate(tokenUrl.Trim(), UriKind.Absolute, out Uri? tokenUri) && (tokenUri.Scheme == Uri.UriSchemeHttp || tokenUri.Scheme == Uri.UriSchemeHttps))
                o.TokenUri = tokenUri;

            o.Scopes = settings.GetStringList(HttpSettingKeys.Scopes)
                .SelectMany(s => s.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            List<string> methods = SplitTokens(settings.GetStringList(HttpSettingKeys.AllowedMethods)).Select(m => m.ToUpperInvariant()).Where(HttpRequestClassifier.IsMethodToken).ToList();
            if (methods.Any()) o.AllowedMethods = new HashSet<string>(methods, StringComparer.Ordinal);

            List<string> prefixes = new List<string>();
            foreach (string p in settings.GetStringList(HttpSettingKeys.AllowedPathPrefixes))
            {
                if (HttpPathGuard.IsValidPrefix(p, out string _)) prefixes.Add(HttpPathGuard.NormalizePrefix(p));
            }
            if (prefixes.Any()) o.AllowedPathPrefixes = prefixes.Distinct(StringComparer.Ordinal).ToList();
            else if (settings.GetStringList(HttpSettingKeys.AllowedPathPrefixes).Any()) o.AllowedPathPrefixes = new List<string> { "/__hugo_no_valid_prefix__" };

            foreach (string line in settings.GetStringList(HttpSettingKeys.MethodEffectOverrides))
            {
                if (HttpEffectOverride.TryParse(line, out HttpEffectOverride? ov, out string _) && ov != null) o.Overrides.Add(ov);
            }

            o.Timeout = TimeSpan.FromSeconds(settings.GetInt(HttpSettingKeys.TimeoutSeconds, 30, 1, 600));
            o.MaxResponseBytes = settings.GetInt(HttpSettingKeys.MaxResponseBytes, 1048576, 1024, 64 * 1024 * 1024);
            o.MaxRequestBytes = settings.GetInt(HttpSettingKeys.MaxRequestBytes, 1048576, 1, 64 * 1024 * 1024);
            o.FollowRedirects = settings.GetBool(HttpSettingKeys.FollowRedirects, true);
            o.MaxRedirects = settings.GetInt(HttpSettingKeys.MaxRedirects, 5, 0, 20);
            o.VerifyTls = settings.GetBool(HttpSettingKeys.VerifyTls, true);
            return o;
        }

        /// <summary>
        /// The base URL, or an error when it is missing or invalid.
        /// </summary>
        /// <returns>Base URL.</returns>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> when the base URL is unusable.</exception>
        public Uri RequireBaseUri()
        {
            if (BaseUri == null) throw new ConnectorException(ConnectorErrorCodes.InvalidSetting, "The resource's baseUrl is missing or is not an absolute http or https URL without a query or credentials.");
            return BaseUri;
        }

        /// <summary>
        /// Parse a base URL: absolute http or https, no user info, query, or fragment.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="uri">Parsed URL.</param>
        /// <param name="error">Reason on failure.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParseBaseUrl(string? value, out Uri? uri, out string error)
        {
            uri = null;
            error = String.Empty;
            if (String.IsNullOrWhiteSpace(value))
            {
                error = "baseUrl is required, for example https://api.example.com/v1.";
                return false;
            }
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                error = "baseUrl must be an absolute http or https URL, for example https://api.example.com/v1.";
                return false;
            }
            if (!String.IsNullOrEmpty(parsed.UserInfo))
            {
                error = "baseUrl must not contain credentials; bind a secret to an auth slot instead.";
                return false;
            }
            if (!String.IsNullOrEmpty(parsed.Query) || !String.IsNullOrEmpty(parsed.Fragment))
            {
                error = "baseUrl must not contain a query string or fragment.";
                return false;
            }
            uri = parsed;
            return true;
        }

        /// <summary>
        /// Parse a <c>Name: value</c> header line.
        /// </summary>
        /// <param name="line">Line.</param>
        /// <param name="name">Header name.</param>
        /// <param name="value">Header value.</param>
        /// <param name="error">Reason on failure.</param>
        /// <returns>True when valid.</returns>
        public static bool TryParseHeaderLine(string? line, out string name, out string value, out string error)
        {
            name = String.Empty;
            value = String.Empty;
            error = String.Empty;
            string text = line ?? String.Empty;
            int colon = text.IndexOf(':');
            if (colon <= 0)
            {
                error = "Header '" + text.Trim() + "' must look like Name: value.";
                return false;
            }
            name = text.Substring(0, colon).Trim();
            value = text.Substring(colon + 1).Trim();
            if (!HttpRequestClassifier.IsToken(name))
            {
                error = "Header name '" + name + "' contains characters that are not allowed.";
                return false;
            }
            if (value.Any(c => c == '\r' || c == '\n' || c == '\0'))
            {
                error = "Header '" + name + "' has a value with line breaks or NUL characters.";
                return false;
            }
            return true;
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<string> SplitTokens(IEnumerable<string> lines)
        {
            return lines.SelectMany(l => l.Split(new char[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        #endregion
    }
}
