namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Typed client for the Papertrail HTTP API (search, systems, groups, saved searches). Authenticates with the
    /// <c>X-Papertrail-Token</c> header. Create one per call; thread-safe.
    /// </summary>
    public class PapertrailClient
    {
        #region Public-Members

        /// <summary>
        /// Provider display name.
        /// </summary>
        public static readonly string ProviderName = "Papertrail";

        /// <summary>
        /// Resolved settings.
        /// </summary>
        public PapertrailSettings Settings { get; }

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
        public PapertrailClient(PapertrailSettings settings, string apiToken, IConnectorLogger? logger)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            if (String.IsNullOrEmpty(apiToken)) throw new ArgumentNullException(nameof(apiToken));
            if (!LoggingHttpTransport.IsHttpUrl(settings.BaseUrl))
                throw new ConnectorException("setting_invalid", "Setting baseUrl '" + settings.BaseUrl + "' is not an absolute http or https URL.");
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            headers["X-Papertrail-Token"] = apiToken;
            _Transport = new LoggingHttpTransport(settings.BaseUrl, ProviderName, headers, logger);
        }

        /// <summary>
        /// Build a client for a resource.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        /// <exception cref="ConnectorException">Thrown when the token is missing or the base URL is invalid.</exception>
        public static PapertrailClient FromResource(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            return new PapertrailClient(PapertrailSettings.FromResource(resource), ApiTokenSlot.Read(resource), resource.Logger);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Search events.
        /// </summary>
        /// <param name="request">Search parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response with a non-null event list.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ConnectorException">Thrown for upstream failures.</exception>
        public async Task<PapertrailSearchResponse> SearchAsync(PapertrailSearchRequest request, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(request);
            string body = await _Transport.GetAsync("events/search.json", request.ToQuery(), token).ConfigureAwait(false);
            PapertrailSearchResponse response = LoggingJson.DeserializeUpstream<PapertrailSearchResponse>(body, ProviderName);
            if (response.Events == null) response.Events = new List<PapertrailEvent>();
            return response;
        }

        /// <summary>
        /// List systems.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Systems.</returns>
        /// <exception cref="ConnectorException">Thrown for upstream failures.</exception>
        public async Task<List<PapertrailSystem>> ListSystemsAsync(CancellationToken token)
        {
            string body = await _Transport.GetAsync("systems.json", null, token).ConfigureAwait(false);
            return LoggingJson.DeserializeUpstream<List<PapertrailSystem>>(body, ProviderName);
        }

        /// <summary>
        /// List groups.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Groups.</returns>
        /// <exception cref="ConnectorException">Thrown for upstream failures.</exception>
        public async Task<List<PapertrailGroup>> ListGroupsAsync(CancellationToken token)
        {
            string body = await _Transport.GetAsync("groups.json", null, token).ConfigureAwait(false);
            return LoggingJson.DeserializeUpstream<List<PapertrailGroup>>(body, ProviderName);
        }

        /// <summary>
        /// List saved searches.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Saved searches.</returns>
        /// <exception cref="ConnectorException">Thrown for upstream failures.</exception>
        public async Task<List<PapertrailSavedSearch>> ListSavedSearchesAsync(CancellationToken token)
        {
            string body = await _Transport.GetAsync("searches.json", null, token).ConfigureAwait(false);
            return LoggingJson.DeserializeUpstream<List<PapertrailSavedSearch>>(body, ProviderName);
        }

        /// <summary>
        /// Resolve a group reference to the numeric ID the search API requires. Numeric values pass through; names
        /// are matched case-insensitively against <c>groups.json</c>.
        /// </summary>
        /// <param name="group">Group ID or name, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Numeric ID, or null when group is null or empty.</returns>
        /// <exception cref="ConnectorException">Thrown with code <c>not_found</c> when no group has that name.</exception>
        public async Task<string?> ResolveGroupIdAsync(string? group, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(group)) return null;
            string g = group.Trim();
            if (Int64.TryParse(g, NumberStyles.None, CultureInfo.InvariantCulture, out long _)) return g;
            List<PapertrailGroup> groups = await ListGroupsAsync(token).ConfigureAwait(false);
            PapertrailGroup? match = groups.FirstOrDefault(x => String.Equals(x.Name, g, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new ConnectorException("not_found", "Papertrail group '" + g + "' was not found. Call logs_list_groups to see group names and IDs.");
            return match.Id.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
