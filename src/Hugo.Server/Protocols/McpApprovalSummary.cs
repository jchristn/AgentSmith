namespace Hugo.Server.Protocols
{
    using System;
    using Hugo.Core.Enums;
    using Hugo.Core.Models;

    /// <summary>
    /// One approval as hugo_list_approvals and hugo://approvals/pending show it.
    /// </summary>
    public class McpApprovalSummary
    {
        /// <summary>
        /// Approval identifier.
        /// </summary>
        public string ApprovalId { get; set; } = String.Empty;

        /// <summary>
        /// Tool.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Resource alias.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Summary of the action.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Effect (Write or Destructive).
        /// </summary>
        public string Effect { get; set; } = String.Empty;

        /// <summary>
        /// Status.
        /// </summary>
        public ApprovalStatusEnum Status { get; set; } = ApprovalStatusEnum.Pending;

        /// <summary>
        /// Requested at.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Expires at.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Who decided it, or null.
        /// </summary>
        public string? DecidedBy { get; set; } = null;

        /// <summary>
        /// Decision note, or null.
        /// </summary>
        public string? DecisionNote { get; set; } = null;

        /// <summary>
        /// Summarize an approval.
        /// </summary>
        /// <param name="approval">Approval.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when approval is null.</exception>
        public static McpApprovalSummary From(Approval approval)
        {
            if (approval == null) throw new ArgumentNullException(nameof(approval));
            McpApprovalSummary s = new McpApprovalSummary();
            s.ApprovalId = approval.Id;
            s.ToolName = approval.ToolName;
            s.Resource = approval.BindingAlias;
            s.Summary = approval.Summary;
            s.Effect = approval.Effect.ToString();
            s.Status = approval.Status;
            s.CreatedUtc = approval.CreatedUtc;
            s.ExpiresUtc = approval.ExpiresUtc;
            s.DecidedBy = approval.DecidedByName;
            s.DecisionNote = approval.DecisionNote;
            return s;
        }
    }
}
