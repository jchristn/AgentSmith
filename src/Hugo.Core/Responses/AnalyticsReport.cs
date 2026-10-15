namespace Hugo.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response of the analytics routes (P6.18): window totals, a gap-filled bucket series, and, for the tools and models
    /// reports, per-tool or per-model rows. Not thread-safe.
    /// </summary>
    public class AnalyticsReport
    {
        /// <summary>
        /// Report kind: overview, tokens, tools, or models.
        /// </summary>
        public string Kind { get; set; } = "overview";

        /// <summary>
        /// Window start (UTC, inclusive, aligned to the interval).
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Window end (UTC, exclusive).
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Bucket width: minute, hour, or day.
        /// </summary>
        public string Interval { get; set; } = "hour";

        /// <summary>
        /// Conversations started in the window.
        /// </summary>
        public long Conversations { get; set; } = 0;

        /// <summary>
        /// Window totals (StartUtc is the window start).
        /// </summary>
        public AnalyticsBucket Totals { get; set; } = new AnalyticsBucket();

        /// <summary>
        /// Approvals requested in the window.
        /// </summary>
        public long ApprovalsRequested { get; set; } = 0;

        /// <summary>
        /// Buckets from the window start to its end, one per interval.
        /// </summary>
        public List<AnalyticsBucket> Buckets { get; set; } = new List<AnalyticsBucket>();

        /// <summary>
        /// Per-tool rows, busiest first (tools report), or null.
        /// </summary>
        public List<AnalyticsToolRow>? Tools { get; set; } = null;

        /// <summary>
        /// Per-model rows, busiest first (models report), or null.
        /// </summary>
        public List<AnalyticsModelRow>? Models { get; set; } = null;
    }
}
