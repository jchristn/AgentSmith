namespace Hugo.Server.Protocols
{
    /// <summary>
    /// Arguments of hugo_await_approval.
    /// </summary>
    public class McpApprovalArguments
    {
        /// <summary>
        /// Approval identifier.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Seconds to wait (0 to 600), or null for Approvals.McpWaitSeconds.
        /// </summary>
        public int? WaitSeconds { get; set; } = null;
    }
}
