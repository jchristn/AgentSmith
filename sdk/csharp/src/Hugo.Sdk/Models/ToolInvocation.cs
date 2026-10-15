namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A recorded tool invocation.
    /// </summary>
    public class ToolInvocation
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
        /// Conversation Identifier.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Message Identifier.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Chat, Mcp, A2A, OpenAI, or Rest.
        /// </summary>
        public string? Surface { get; set; } = null;

        /// <summary>
        /// Principal type.
        /// </summary>
        public string? PrincipalType { get; set; } = null;

        /// <summary>
        /// Principal Identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Principal name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

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
        /// Connector type.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>
        /// Arguments after secret redaction (JSON).
        /// </summary>
        public string? ArgumentsRedacted { get; set; } = null;

        /// <summary>
        /// Redaction applied.
        /// </summary>
        public bool RedactionApplied { get; set; } = false;

        /// <summary>
        /// Read, Write, or Destructive.
        /// </summary>
        public string? Effect { get; set; } = null;

        /// <summary>
        /// Effect reason.
        /// </summary>
        public string? EffectReason { get; set; } = null;

        /// <summary>
        /// Access mode.
        /// </summary>
        public string? AccessMode { get; set; } = null;

        /// <summary>
        /// Allowed, Denied, ApprovalRequired, Approved, Rejected, Expired, or Cancelled.
        /// </summary>
        public string? Decision { get; set; } = null;

        /// <summary>
        /// Decision code.
        /// </summary>
        public string? DecisionCode { get; set; } = null;

        /// <summary>
        /// Decision reason.
        /// </summary>
        public string? DecisionReason { get; set; } = null;

        /// <summary>
        /// Approval Identifier.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Pending, Running, Succeeded, Failed, TimedOut, Cancelled, or NotExecuted.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Error code.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message.
        /// </summary>
        public string? ErrorMessage { get; set; } = null;

        /// <summary>
        /// Output bytes.
        /// </summary>
        public long OutputBytes { get; set; } = 0;

        /// <summary>
        /// Output truncated.
        /// </summary>
        public bool OutputTruncated { get; set; } = false;

        /// <summary>
        /// Output preview.
        /// </summary>
        public string? OutputPreview { get; set; } = null;

        /// <summary>
        /// Duration milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Trace Identifier.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// Correlation Identifier.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

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
