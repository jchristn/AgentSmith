namespace Hugo.Connectors.Logging
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Resolved settings of a <c>solarwinds-observability</c> resource. Treat as immutable.
    /// </summary>
    public class SwoSettings
    {
        #region Public-Members

        /// <summary>
        /// Data centers with a public API host (<c>api.{dc}.cloud.solarwinds.com</c>).
        /// </summary>
        public static readonly IReadOnlyList<string> DataCenters = new List<string> { "na-01", "na-02", "eu-01", "ap-01" };

        /// <summary>
        /// Default data center.
        /// </summary>
        public static readonly string DefaultDataCenter = "na-01";

        /// <summary>
        /// Default <c>maxEvents</c>.
        /// </summary>
        public static readonly int DefaultMaxEvents = 500;

        /// <summary>
        /// Default <c>maxTimeRangeHours</c> (one week).
        /// </summary>
        public static readonly int DefaultMaxTimeRangeHours = 168;

        /// <summary>
        /// Data center. Default <c>na-01</c>.
        /// </summary>
        public string DataCenter { get; set; } = DefaultDataCenter;

        /// <summary>
        /// Base URL override, or null to derive it from the data center.
        /// </summary>
        public string? BaseUrlOverride { get; set; } = null;

        /// <summary>
        /// Log group searched when a call names none, or null.
        /// </summary>
        public string? DefaultGroup { get; set; } = null;

        /// <summary>
        /// Maximum events per call. Minimum 1, maximum 10000, default 500.
        /// </summary>
        public int MaxEvents { get; set; } = DefaultMaxEvents;

        /// <summary>
        /// Maximum search window in hours. Minimum 1, maximum 8760, default 168.
        /// </summary>
        public int MaxTimeRangeHours { get; set; } = DefaultMaxTimeRangeHours;

        /// <summary>
        /// Effective API base URL including <c>/v1</c>, without a trailing slash.
        /// </summary>
        public string BaseUrl
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(BaseUrlOverride)) return BaseUrlOverride.Trim().TrimEnd('/');
                return BaseUrlFor(DataCenter);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Public API base URL for a data center.
        /// </summary>
        /// <param name="dataCenter">Data center (for example <c>eu-01</c>).</param>
        /// <returns>Base URL, for example <c>https://api.eu-01.cloud.solarwinds.com/v1</c>.</returns>
        public static string BaseUrlFor(string dataCenter)
        {
            string dc = String.IsNullOrWhiteSpace(dataCenter) ? DefaultDataCenter : dataCenter.Trim().ToLowerInvariant();
            return "https://api." + dc + ".cloud.solarwinds.com/v1";
        }

        /// <summary>
        /// Whether a data center is known.
        /// </summary>
        /// <param name="dataCenter">Data center.</param>
        /// <returns>True when known.</returns>
        public static bool IsKnownDataCenter(string? dataCenter)
        {
            return dataCenter != null && DataCenters.Contains(dataCenter.Trim().ToLowerInvariant(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Read settings from a resource, applying defaults and clamping ranges.
        /// </summary>
        /// <param name="resource">Resource.</param>
        /// <returns>Settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when resource is null.</exception>
        public static SwoSettings FromResource(ConnectorResourceContext resource)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ResourceSettings s = resource.Settings;
            return new SwoSettings
            {
                DataCenter = (s.GetString("dataCenter", DefaultDataCenter) ?? DefaultDataCenter).Trim().ToLowerInvariant(),
                BaseUrlOverride = s.GetString("baseUrl"),
                DefaultGroup = s.GetString("defaultGroup"),
                MaxEvents = s.GetInt("maxEvents", DefaultMaxEvents, 1, 10000),
                MaxTimeRangeHours = s.GetInt("maxTimeRangeHours", DefaultMaxTimeRangeHours, 1, 8760)
            };
        }

        #endregion
    }
}
