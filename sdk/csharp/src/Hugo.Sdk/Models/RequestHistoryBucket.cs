namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// One time bucket of the request history summary.
    /// </summary>
    public class RequestHistoryBucket
    {
        #region Public-Members

        /// <summary>
        /// Bucket start UTC.
        /// </summary>
        public DateTime BucketStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Bucket end UTC.
        /// </summary>
        public DateTime BucketEndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Success count.
        /// </summary>
        public long SuccessCount { get; set; } = 0;

        /// <summary>
        /// Failure count.
        /// </summary>
        public long FailureCount { get; set; } = 0;

        /// <summary>
        /// Average duration milliseconds.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;

        #endregion
    }
}
