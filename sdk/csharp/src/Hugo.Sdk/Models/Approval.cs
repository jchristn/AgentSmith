namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// An approval request for a tool call.
    /// </summary>
    public class Approval
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Tool invocation Identifier.
        /// </summary>
        public string? ToolInvocationId { get; set; } = null;

        /// <summary>
        /// Agent profile Identifier.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Binding Identifier.
        /// </summary>
        public string? BindingId { get; set; } = null;

        /// <summary>
        /// Binding alias.
        /// </summary>
        public string? BindingAlias { get; set; } = null;

        /// <summary>
        /// Resource Identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Surface.
        /// </summary>
        public string? Surface { get; set; } = null;

        /// <summary>
        /// Requested by principal type.
        /// </summary>
        public string? RequestedByPrincipalType { get; set; } = null;

        /// <summary>
        /// Requested by principal Identifier.
        /// </summary>
        public string? RequestedByPrincipalId { get; set; } = null;

        /// <summary>
        /// Requested by name.
        /// </summary>
        public string? RequestedByName { get; set; } = null;

        /// <summary>
        /// Human-readable action.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// Effect.
        /// </summary>
        public string? Effect { get; set; } = null;

        /// <summary>
        /// Effect reason.
        /// </summary>
        public string? EffectReason { get; set; } = null;

        /// <summary>
        /// Arguments redacted.
        /// </summary>
        public string? ArgumentsRedacted { get; set; } = null;

        /// <summary>
        /// Pending, Approved, Rejected, Expired, or Cancelled.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Decided by user Identifier.
        /// </summary>
        public string? DecidedByUserId { get; set; } = null;

        /// <summary>
        /// Decided by name.
        /// </summary>
        public string? DecidedByName { get; set; } = null;

        /// <summary>
        /// Decision note.
        /// </summary>
        public string? DecisionNote { get; set; } = null;

        /// <summary>
        /// Decided UTC.
        /// </summary>
        public DateTime? DecidedUtc { get; set; } = null;

        /// <summary>
        /// Expires UTC.
        /// </summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>
        /// The tool invocation, with its result once executed.
        /// </summary>
        public ToolInvocation? Invocation { get; set; } = null;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp, UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
