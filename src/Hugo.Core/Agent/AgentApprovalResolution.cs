namespace Hugo.Core.Agent
{
    using System;

    /// <summary>
    /// How a pending approval ended.
    /// </summary>
    public class AgentApprovalResolution
    {
        /// <summary>
        /// Approval ID.
        /// </summary>
        public string ApprovalId { get; set; } = String.Empty;

        /// <summary>
        /// Outcome: Approved, Rejected, or Expired.
        /// </summary>
        public AgentToolDecisionEnum Decision { get; set; } = AgentToolDecisionEnum.Approved;

        /// <summary>
        /// Who decided (user ID or display name), when known.
        /// </summary>
        public string? DecidedBy { get; set; } = null;

        /// <summary>
        /// Optional comment from the approver.
        /// </summary>
        public string? Comment { get; set; } = null;
    }
}
