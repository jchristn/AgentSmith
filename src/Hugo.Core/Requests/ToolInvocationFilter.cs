namespace Hugo.Core.Requests
{
    using System;
    using Hugo.Core.Enums;

    /// <summary>
    /// Filters for listing tool invocations and approvals. Null fields do not filter.
    /// </summary>
    public class ToolInvocationFilter
    {
        /// <summary>
        /// Profile identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Resource identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Principal identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Decision.
        /// </summary>
        public InvocationDecisionEnum? Decision { get; set; } = null;

        /// <summary>
        /// Execution status.
        /// </summary>
        public InvocationStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Surface.
        /// </summary>
        public InvocationSurfaceEnum? Surface { get; set; } = null;

        /// <summary>
        /// Approval status (approvals only).
        /// </summary>
        public ApprovalStatusEnum? ApprovalStatus { get; set; } = null;

        /// <summary>
        /// Only records created at or after this time (UTC).
        /// </summary>
        public DateTime? SinceUtc { get; set; } = null;
    }
}
