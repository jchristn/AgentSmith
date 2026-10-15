namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Time-bucketed request counts.
    /// </summary>
    public class RequestHistorySummary
    {
        #region Public-Members

        /// <summary>
        /// Total count.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Total success.
        /// </summary>
        public long TotalSuccess { get; set; } = 0;

        /// <summary>
        /// Total failure.
        /// </summary>
        public long TotalFailure { get; set; } = 0;

        /// <summary>
        /// Average duration milliseconds.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;

        /// <summary>
        /// Buckets.
        /// </summary>
        public List<RequestHistoryBucket> Buckets { get; set; } = new List<RequestHistoryBucket>();

        #endregion
    }
}
