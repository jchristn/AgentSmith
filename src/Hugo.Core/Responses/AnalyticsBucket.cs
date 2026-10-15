namespace Hugo.Core.Responses
{
    using System;

    /// <summary>
    /// One time bucket of an analytics series. Buckets without activity are present with zeros (server-side gap fill).
    /// Not thread-safe.
    /// </summary>
    public class AnalyticsBucket
    {
        /// <summary>
        /// Bucket start (UTC, inclusive).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Agent turns that ended in the bucket.
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
        /// Tool calls that failed or were denied.
        /// </summary>
        public long ToolErrors { get; set; } = 0;

        /// <summary>
        /// Tool calls that went through an approval.
        /// </summary>
        public long Approvals { get; set; } = 0;

        /// <summary>
        /// Mean time to first token of the bucket's turns in milliseconds, or null.
        /// </summary>
        public double? AvgTimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Mean duration of the bucket's turns in milliseconds, or null.
        /// </summary>
        public double? AvgTurnDurationMs { get; set; } = null;
    }
}
