namespace Hugo.Sdk.Requests
{
    using System;

    /// <summary>
    /// Approval list filter.
    /// </summary>
    public class ApprovalFilter
    {
        #region Public-Members

        /// <summary>
        /// Pending, Approved, Rejected, Expired, or Cancelled.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Agent profile Identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Principal Identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Effect of the gated call: Read, Write, or Destructive.
        /// </summary>
        public string? Effect { get; set; } = null;

        /// <summary>
        /// Only approvals created at or after this time, UTC.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Only approvals created before this time, UTC.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        #endregion
    }
}
