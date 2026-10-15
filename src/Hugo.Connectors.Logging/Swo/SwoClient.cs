namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed client for the SolarWinds Observability public REST API (logs, entities, metrics). Authenticates with a
    /// bearer API token. Create one per call; thread-safe.
    /// </summary>
    public class SwoClient
    {
        #region Public-Members

        /// <summary>
        /// Provider display name.
        /// </summary>
        public static readonly string ProviderName = "SolarWinds Observability";

        /// <summary>
        /// Resolved settings.
        /// </summary>
        public SwoSettings Settings { get; }

        #endregion

        #region Private-Members

        private readonly LoggingHttpTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="apiToken">API token.</param>
        /// <param name="logger">Logger, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or apiToken is null.</exception>
        /// <exception cref="ConnectorException">Thrown with code <c>setting_invalid</c> when the base URL is not an http or https URL.</exception>
        public SwoClient(SwoSettings settings, string apiToken, IConnectorLogger? logger)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (String.IsNullOrEmpty(apiToken)) throw new ArgumentNullException(nameof(apiToken));
            if (!LoggingHttpTransport.IsHttpUrl(settings.BaseUrl))
                throw new ConnectorException("setting_invalid", "SolarWinds Observability base URL '" + settings.BaseUrl + "' is not an absolute http or https URL.");
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers["Authorization"] = "Bearer " + apiToken;
            _Transport = new LoggingHttpTransport(settings.BaseUrl, ProviderName, headers, logger);
        }

        /// <summary>
        /// Build a client for a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when the token is missing or the base URL is invalid.</exception>
        public static SwoClient FromResource(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return new SwoClient(SwoSettings.FromResource(resource), ApiTokenSlot.Read(resource), resource.Logger);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract the skip token from <c>pageInfo.nextPage</c>. The OpenAPI document calls the value a token, but it
        /// may be a link with a <c>skipToken</c> query parameter, so both forms are accepted.
        /// </summary>
        /// <param name="nextPage">Value of <c>nextPage</c>.</param>
        /// <returns>Token, or null when there is no next page.</returns>
        public static string? ExtractSkipToken(string? nextPage)
        {
            if (String.IsNullOrWhiteSpace(nextPage)) return null;
            string value = nextPage.Trim();
            int q = value.IndexOf('?');
            if (q < 0 && !value.Contains("skipToken=", StringComparison.Ordinal)) return value;
            string query = q >= 0 ? value.Substring(q + 1) : value;
            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (String.Equals(part.Substring(0, eq), "skipToken", StringComparison.Ordinal))
                {
                    string token = Uri.UnescapeDataString(part.Substring(eq + 1));
                    return String.IsNullOrEmpty(token) ? null : token;
                }
            }
            return q >= 0 ? null : value;
        }

        /// <summary>
        /// Search logs.
        /// </summary>
        /// <param name="query">Parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response with a non-null event list.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        /// <exception cref="ConnectorException">Thrown for upstream failures.</exception>
        public async Task<SwoLogsResponse> SearchLogsAsync(SwoLogQuery query, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(query);
            string body = await _Transport.GetAsync("logs", query.ToQuery(), token).ConfigureAwait(false);
            SwoLogsResponse response = LoggingJson.DeserializeUpstream<SwoLogsResponse>(body, ProviderName);
            if (response.Logs == null) response.Logs = new List<SwoLogEvent>();
            return response;
        }

        /// <summary>
        /// List entities.
        /// </summary>
        /// <param name="type">Entity type, or null for all.</param>
        /// <param name="name">Case-insensitive name substring, or null.</param>
        /// <param name="search">Contextual search, or null.</param>
        /// <param name="pageSize">Page size. Minimum 1, maximum 1000.</param>
        /// <param name="skipToken">Page token, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response with a non-null entity list.</returns>
        /// <exception cref="ConnectorException">Thrown for upstream failures.</exception>
        public async Task<SwoEntitiesResponse> ListEntitiesAsync(string? type, string? name, string? search, int pageSize, string? skipToken, CancellationToken token)
        {
            List<KeyValuePair<string, string?>> q = new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("type", type),
                new KeyValuePair<string, string?>("name", name),
                new KeyValuePair<string, string?>("search", search),
                new KeyValuePair<string, string?>("pageSize", Math.Clamp(pageSize, 1, 1000).ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string?>("skipToken", skipToken)
            };
            string body = await _Transport.GetAsync("entities", q, token).ConfigureAwait(false);
            SwoEntitiesResponse response = LoggingJson.DeserializeUpstream<SwoEntitiesResponse>(body, ProviderName);
            if (response.Entities == null) response.Entities = new List<SwoEntity>();
            return response;
        }

        /// <summary>
        /// Get one entity.
        /// </summary>
        /// <param name="id">Entity ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entity.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="ConnectorException">Thrown for upstream failures, including <c>not_found</c>.</exception>
        public async Task<SwoEntity> GetEntityAsync(string id, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            string body = await _Transport.GetAsync("entities/" + Uri.EscapeDataString(id.Trim()), null, token).ConfigureAwait(false);
            return LoggingJson.DeserializeUpstream<SwoEntity>(body, ProviderName);
        }

        /// <summary>
        /// Query metric measurements.
        /// </summary>
        /// <param name="query">Parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response with a non-null grouping list.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        /// <exception cref="ConnectorException">Thrown for upstream failures.</exception>
        public async Task<SwoMeasurementsResponse> QueryMetricAsync(SwoMetricQuery query, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(query);
            if (String.IsNullOrWhiteSpace(query.Name)) throw new ConnectorException("invalid_argument", "Argument 'name' (metric name) is required.");
            string body = await _Transport.GetAsync("metrics/" + Uri.EscapeDataString(query.Name.Trim()) + "/measurements", query.ToQuery(), token).ConfigureAwait(false);
            SwoMeasurementsResponse response = LoggingJson.DeserializeUpstream<SwoMeasurementsResponse>(body, ProviderName);
            if (response.Groupings == null) response.Groupings = new List<SwoGrouping>();
            return response;
        }

        #endregion
    }
}
