namespace Hugo.Server.Protocols
{
    using Hugo.Core.Enums;

    /// <summary>
    /// Arguments of hugo_list_approvals.
    /// </summary>
    public class McpListApprovalsArguments
    {
        /// <summary>
        /// Only approvals with this status, or null for all.
        /// </summary>
        public ApprovalStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Most approvals to return (1 to 100), or null for 25.
        /// </summary>
        public int? MaxResults { get; set; } = null;
    }
}
