namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// Totals for one analytics time bucket (or the whole window).
    /// </summary>
    public class AnalyticsBucket
    {
        #region Public-Members

        /// <summary>
        /// Bucket start, UTC (for window totals, the window start).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Agent turns.
        /// </summary>
        public long Turns { get; set; } = 0;

        /// <summary>
        /// Input tokens.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Tool calls.
        /// </summary>
        public long ToolCalls { get; set; } = 0;

        /// <summary>
        /// Tool calls that failed.
        /// </summary>
        public long ToolErrors { get; set; } = 0;

        /// <summary>
        /// Approvals requested.
        /// </summary>
        public long Approvals { get; set; } = 0;

        /// <summary>
        /// Average time to first token, milliseconds.
        /// </summary>
        public double AvgTimeToFirstTokenMs { get; set; } = 0;

        /// <summary>
        /// Average turn duration, milliseconds.
        /// </summary>
        public double AvgTurnDurationMs { get; set; } = 0;

        #endregion
    }
}
