namespace Hugo.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Counts of all audit records matching a filter, independent of pagination.
    /// </summary>
    public class AuditSummary
    {
        /// <summary>Total matching records. Zero when no records match.</summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>Counts keyed by event type. Only event types with matches are present.</summary>
        public Dictionary<string, long> EventCounts { get; set; } = new Dictionary<string, long>();
    }
}
