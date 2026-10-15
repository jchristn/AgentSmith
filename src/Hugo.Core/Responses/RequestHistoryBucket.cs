namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// One summary bucket.
    /// </summary>
    public class RequestHistoryBucket
    {
        /// <summary>
        /// Bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Bucket end (UTC).
        /// </summary>
        public DateTime BucketEndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Requests with status below 400.
        /// </summary>
        public long SuccessCount { get; set; } = 0;

        /// <summary>
        /// Requests with status 400 or above.
        /// </summary>
        public long FailureCount { get; set; } = 0;

        /// <summary>
        /// Average duration.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;
    }
}
