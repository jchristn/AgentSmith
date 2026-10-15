namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Analytics report for a time window (GET /v1.0/api/analytics/{overview|tokens|tools|models}).
    /// </summary>
    public class AnalyticsReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind: overview, tokens, tools, or models.
        /// </summary>
        public string Kind { get; set; } = String.Empty;

        /// <summary>
        /// Window start, UTC.
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Window end, UTC.
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Bucket size: minute, hour, or day.
        /// </summary>
        public string Interval { get; set; } = String.Empty;

        /// <summary>
        /// Conversations active in the window.
        /// </summary>
        public long Conversations { get; set; } = 0;

        /// <summary>
        /// Window totals.
        /// </summary>
        public AnalyticsBucket Totals { get; set; } = new AnalyticsBucket();

        /// <summary>
        /// Approvals requested in the window.
        /// </summary>
        public long ApprovalsRequested { get; set; } = 0;

        /// <summary>
        /// Per-bucket totals, oldest first.
        /// </summary>
        public List<AnalyticsBucket> Buckets
        {
            get
            {
                return _Buckets;
            }
            set
            {
                _Buckets = value ?? new List<AnalyticsBucket>();
            }
        }

        /// <summary>
        /// Per-tool rows.
        /// </summary>
        public List<AnalyticsToolRow> Tools
        {
            get
            {
                return _Tools;
            }
            set
            {
                _Tools = value ?? new List<AnalyticsToolRow>();
            }
        }

        /// <summary>
        /// Per-model-endpoint rows.
        /// </summary>
        public List<AnalyticsModelRow> Models
        {
            get
            {
                return _Models;
            }
            set
            {
                _Models = value ?? new List<AnalyticsModelRow>();
            }
        }

        #endregion

        #region Private-Members

        private List<AnalyticsBucket> _Buckets = new List<AnalyticsBucket>();
        private List<AnalyticsToolRow> _Tools = new List<AnalyticsToolRow>();
        private List<AnalyticsModelRow> _Models = new List<AnalyticsModelRow>();

        #endregion
    }
}
