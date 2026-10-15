namespace Hugo.Sdk.Responses
{
    using System;

    /// <summary>
    /// Server health.
    /// </summary>
    public class HealthResponse
    {
        #region Public-Members

        /// <summary>
        /// healthy, degraded, migrating, draining, or alive.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Node.
        /// </summary>
        public string? Node { get; set; } = null;

        /// <summary>
        /// Version.
        /// </summary>
        public string? Version { get; set; } = null;

        /// <summary>
        /// Database.
        /// </summary>
        public string? Database { get; set; } = null;

        /// <summary>
        /// Schema version.
        /// </summary>
        public int SchemaVersion { get; set; } = 0;

        /// <summary>
        /// Expected schema version.
        /// </summary>
        public int ExpectedSchemaVersion { get; set; } = 0;

        /// <summary>
        /// Background.
        /// </summary>
        public string? Background { get; set; } = null;

        /// <summary>
        /// UTC.
        /// </summary>
        public DateTime? Utc { get; set; } = null;

        #endregion
    }
}
