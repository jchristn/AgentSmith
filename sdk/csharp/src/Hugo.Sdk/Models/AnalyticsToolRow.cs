namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Per-tool analytics row.
    /// </summary>
    public class AnalyticsToolRow
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Connector type of the tool, when known.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Calls.
        /// </summary>
        public long Calls { get; set; } = 0;

        /// <summary>
        /// Calls that failed.
        /// </summary>
        public long Errors { get; set; } = 0;

        /// <summary>
        /// Calls denied by policy.
        /// </summary>
        public long Denied { get; set; } = 0;

        /// <summary>
        /// Calls that required approval.
        /// </summary>
        public long Approvals { get; set; } = 0;

        /// <summary>
        /// Average duration, milliseconds.
        /// </summary>
        public double AvgDurationMs { get; set; } = 0;

        #endregion
    }
}
