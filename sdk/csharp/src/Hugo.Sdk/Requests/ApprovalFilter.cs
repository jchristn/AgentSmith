namespace Hugo.Sdk.Requests
{
    using Hugo.Sdk.Models;

    /// <summary>
    /// Approval list filter (preview).
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

        #endregion
    }
}
