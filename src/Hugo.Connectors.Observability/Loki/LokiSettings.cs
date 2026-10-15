namespace Hugo.Connectors.Observability
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Settings of a <c>loki</c> resource. Immutable after construction and thread-safe.
    /// </summary>
    public class LokiSettings
    {
        #region Public-Members

        /// <summary>
        /// Connection settings.
        /// </summary>
        public ObservabilityEndpoint Endpoint { get; }

        /// <summary>
        /// Tenant ID sent as <c>X-Scope-OrgID</c>, or null to send no tenant header.
        /// </summary>
        public string? TenantId { get; }

        /// <summary>
        /// Most log lines one call returns. Minimum 1, maximum 5000, default 1000.
        /// </summary>
        public int MaxLines { get; }

        /// <summary>
        /// Longest window one call may cover, in hours. Minimum 1, maximum 8760, default 24.
        /// </summary>
        public int MaxRangeHours { get; }

        /// <summary>
        /// Header carrying the tenant ID.
        /// </summary>
        public static readonly string TenantHeaderName = "X-Scope-OrgID";

        /// <summary>
        /// Default maximum lines.
        /// </summary>
        public static readonly int DefaultMaxLines = 1000;

        /// <summary>
        /// Default maximum range in hours.
        /// </summary>
        public static readonly int DefaultMaxRangeHours = 24;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse from resource settings.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="ConnectorException">Thrown with <c>invalid_setting</c> for an invalid baseUrl or authType.</exception>
        public LokiSettings(ResourceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Endpoint = new ObservabilityEndpoint(settings);
            string? tenant = settings.GetString(ObservabilitySettingKeys.TenantHeader);
            TenantId = String.IsNullOrWhiteSpace(tenant) ? null : tenant.Trim();
            MaxLines = settings.GetInt(ObservabilitySettingKeys.MaxLines, DefaultMaxLines, 1, 5000);
            MaxRangeHours = settings.GetInt(ObservabilitySettingKeys.MaxRangeHours, DefaultMaxRangeHours, 1, 8760);
        }

        #endregion
    }
}
