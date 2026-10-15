namespace Hugo.Server.Protocols
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of hugo_list_approvals.
    /// </summary>
    public class McpApprovalList
    {
        /// <summary>
        /// Approvals, newest first.
        /// </summary>
        public List<McpApprovalSummary> Approvals { get; set; } = new List<McpApprovalSummary>();

        /// <summary>
        /// Total matching approvals.
        /// </summary>
        public long TotalRecords { get; set; } = 0;
    }
}
