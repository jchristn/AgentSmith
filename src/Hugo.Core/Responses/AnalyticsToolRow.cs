namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// Tool usage over an analytics window. Not thread-safe.
    /// </summary>
    public class AnalyticsToolRow
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Connector type, when known.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Calls.
        /// </summary>
        public long Calls { get; set; } = 0;

        /// <summary>
        /// Calls that failed after running.
        /// </summary>
        public long Errors { get; set; } = 0;

        /// <summary>
        /// Calls refused by the pipeline (Denied, Rejected, Expired).
        /// </summary>
        public long Denied { get; set; } = 0;

        /// <summary>
        /// Calls that went through an approval.
        /// </summary>
        public long Approvals { get; set; } = 0;

        /// <summary>
        /// Mean execution time of calls that ran, in milliseconds, or null.
        /// </summary>
        public double? AvgDurationMs { get; set; } = null;
    }
}
