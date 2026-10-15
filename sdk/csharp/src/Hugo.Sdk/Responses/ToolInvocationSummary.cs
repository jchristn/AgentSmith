namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Tool invocation counts.
    /// </summary>
    public class ToolInvocationSummary
    {
        #region Public-Members

        /// <summary>
        /// Total.
        /// </summary>
        public long Total { get; set; } = 0;

        /// <summary>
        /// Since UTC.
        /// </summary>
        public DateTime? SinceUtc { get; set; } = null;

        /// <summary>
        /// By decision.
        /// </summary>
        public List<StatusCount> ByDecision { get; set; } = new List<StatusCount>();

        /// <summary>
        /// By status.
        /// </summary>
        public List<StatusCount> ByStatus { get; set; } = new List<StatusCount>();

        /// <summary>
        /// By effect.
        /// </summary>
        public List<StatusCount> ByEffect { get; set; } = new List<StatusCount>();

        /// <summary>
        /// By tool.
        /// </summary>
        public List<StatusCount> ByTool { get; set; } = new List<StatusCount>();

        #endregion
    }
}
