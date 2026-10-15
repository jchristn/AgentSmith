namespace Hugo.Connectors.Observability
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Settings of a <c>prometheus</c> resource. Immutable after construction and thread-safe.
    /// </summary>
    public class PrometheusSettings
    {
        #region Public-Members

        /// <summary>
        /// Connection settings.
        /// </summary>
        public ObservabilityEndpoint Endpoint { get; }

        /// <summary>
        /// Most series one call returns. Minimum 1, maximum 10000, default 500.
        /// </summary>
        public int MaxSeries { get; }

        /// <summary>
        /// Most points per series in a range query; the step is raised to stay within it. Minimum 10, maximum 11000,
        /// default 240.
        /// </summary>
        public int MaxPointsPerSeries { get; }

        /// <summary>
        /// Longest window one call may cover, in hours. Minimum 1, maximum 8760, default 168.
        /// </summary>
        public int MaxRangeHours { get; }

        /// <summary>
        /// Default maximum series.
        /// </summary>
        public static readonly int DefaultMaxSeries = 500;

        /// <summary>
        /// Default maximum points per series.
        /// </summary>
        public static readonly int DefaultMaxPointsPerSeries = 240;

        /// <summary>
        /// Default maximum range in hours.
        /// </summary>
        public static readonly int DefaultMaxRangeHours = 168;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse from resource settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> for an invalid baseUrl or authType.</exception>
        public PrometheusSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Endpoint = new ObservabilityEndpoint(settings);
            MaxSeries = settings.GetInt(ObservabilitySettingKeys.MaxSeries, DefaultMaxSeries, 1, 10000);
            MaxPointsPerSeries = settings.GetInt(ObservabilitySettingKeys.MaxPointsPerSeries, DefaultMaxPointsPerSeries, 10, 11000);
            MaxRangeHours = settings.GetInt(ObservabilitySettingKeys.MaxRangeHours, DefaultMaxRangeHours, 1, 8760);
        }

        #endregion
    }
}
