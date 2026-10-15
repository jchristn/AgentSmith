namespace Hugo.Connectors.Logging
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Resolved settings of a <c>papertrail</c> resource. Treat as immutable.
    /// </summary>
    public class PapertrailSettings
    {
        #region Public-Members

        /// <summary>
        /// Default API base URL.
        /// </summary>
        public static readonly string DefaultBaseUrl = "https://papertrailapp.com/api/v1";

        /// <summary>
        /// Default <c>maxEvents</c>.
        /// </summary>
        public static readonly int DefaultMaxEvents = 500;

        /// <summary>
        /// Default <c>maxTimeRangeHours</c> (one week).
        /// </summary>
        public static readonly int DefaultMaxTimeRangeHours = 168;

        /// <summary>
        /// API base URL without a trailing slash.
        /// </summary>
        public string BaseUrl { get; set; } = DefaultBaseUrl;

        /// <summary>
        /// Group ID or name applied when the caller gives none. Null for all systems.
        /// </summary>
        public string? DefaultGroup { get; set; } = null;

        /// <summary>
        /// System ID or name applied when the caller gives none. Null for all systems.
        /// </summary>
        public string? DefaultSystem { get; set; } = null;

        /// <summary>
        /// Maximum events per call. Minimum 1, maximum 10000, default 500.
        /// </summary>
        public int MaxEvents { get; set; } = DefaultMaxEvents;

        /// <summary>
        /// Maximum search window in hours. Minimum 1, maximum 8760, default 168.
        /// </summary>
        public int MaxTimeRangeHours { get; set; } = DefaultMaxTimeRangeHours;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read settings from a resource, applying defaults and clamping ranges.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static PapertrailSettings FromResource(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ResourceSettings s = resource.Settings;
            return new PapertrailSettings
            {
                BaseUrl = (s.GetString("baseUrl", DefaultBaseUrl) ?? DefaultBaseUrl).Trim().TrimEnd('/'),
                DefaultGroup = s.GetString("defaultGroup"),
                DefaultSystem = s.GetString("defaultSystem"),
                MaxEvents = s.GetInt("maxEvents", DefaultMaxEvents, 1, 10000),
                MaxTimeRangeHours = s.GetInt("maxTimeRangeHours", DefaultMaxTimeRangeHours, 1, 8760)
            };
        }

        #endregion
    }
}
