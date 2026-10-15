namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Tool invocation counts for a tenant.
    /// </summary>
    public class ToolInvocationSummary
    {
        /// <summary>
        /// Total invocations.
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// Start of the window (UTC), or null for all time.
        /// </summary>
        public DateTime? SinceUtc { get; set; } = null;

        /// <summary>
        /// Counts by decision.
        /// </summary>
        public List<StatusCount> ByDecision { get; set; } = new List<StatusCount>();

        /// <summary>
        /// Counts by execution status.
        /// </summary>
        public List<StatusCount> ByStatus { get; set; } = new List<StatusCount>();

        /// <summary>
        /// Counts by effect.
        /// </summary>
        public List<StatusCount> ByEffect { get; set; } = new List<StatusCount>();

        /// <summary>
        /// Counts by tool name.
        /// </summary>
        public List<StatusCount> ByTool { get; set; } = new List<StatusCount>();
    }
}
