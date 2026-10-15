namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Filters for listing resources. Null fields do not filter.
    /// </summary>
    public class ResourceFilter
    {
        /// <summary>
        /// Connector type, for example ssh or postgresql (exact match).
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Environment label, for example production (case-insensitive exact match).
        /// </summary>
        public string? Environment { get; set; } = null;

        /// <summary>
        /// Health from the last connection test: Healthy (the test passed), Unhealthy (it failed), or Unknown (never
        /// tested).
        /// </summary>
        public ModelHealthStatusEnum? Health { get; set; } = null;
    }
}
