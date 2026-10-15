namespace Hugo.Core.Models
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;
    using Hugo.Core.Helpers;

    /// <summary>
    /// Record of one tool call through the invocation pipeline (tool_invocations, plan section 7.5). Arguments are stored
    /// redacted and never queried. Not thread-safe.
    /// </summary>
    public class ToolInvocation
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix tiv_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.NewToolInvocationId() : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Conversation, when the call came from a turn.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Message, when known.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Surface.
        /// </summary>
        public InvocationSurfaceEnum Surface { get; set; } = InvocationSurfaceEnum.Rest;

        /// <summary>
        /// Caller principal type.
        /// </summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>
        /// Caller principal identifier.
        /// </summary>
        public string? PrincipalId { get; set; } = null;

        /// <summary>
        /// Caller display name.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Profile identifier, or null for an administrator resource invoke.
        /// </summary>
        public string? AgentProfileId { get; set; } = null;

        /// <summary>
        /// Binding identifier, when resolved.
        /// </summary>
        public string? BindingId { get; set; } = null;

        /// <summary>
        /// Binding alias as requested or resolved.
        /// </summary>
        public string? BindingAlias { get; set; } = null;

        /// <summary>
        /// Resource identifier, when resolved.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Connector type, when resolved.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string ToolName
        {
            get
            {
                return _ToolName;
            }
            set
            {
                _ToolName = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Arguments JSON after secret redaction (unmanaged JSON, plan section 7.6).
        /// </summary>
        public string? ArgumentsRedacted { get; set; } = null;

        /// <summary>
        /// Whether redaction changed the arguments. A call whose arguments were changed by redaction cannot be
        /// resumed from this record after a restart.
        /// </summary>
        public bool RedactionApplied { get; set; } = false;

        /// <summary>
        /// Classified effect, when classification ran.
        /// </summary>
        public EffectEnum? Effect { get; set; } = null;

        /// <summary>
        /// Classifier reason.
        /// </summary>
        public string? EffectReason { get; set; } = null;

        /// <summary>
        /// Effective access mode (after the resource ceiling).
        /// </summary>
        public AccessModeEnum? AccessMode { get; set; } = null;

        /// <summary>
        /// Decision.
        /// </summary>
        public InvocationDecisionEnum Decision { get; set; } = InvocationDecisionEnum.Denied;

        /// <summary>
        /// Stable reason code for the decision (for example readonly_binding).
        /// </summary>
        public string? DecisionCode { get; set; } = null;

        /// <summary>
        /// Human-readable decision reason.
        /// </summary>
        public string? DecisionReason { get; set; } = null;

        /// <summary>
        /// Approval identifier, when approval was involved.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Execution status.
        /// </summary>
        public InvocationStatusEnum Status { get; set; } = InvocationStatusEnum.NotExecuted;

        /// <summary>
        /// Error code of a failed call.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message of a failed call (scrubbed).
        /// </summary>
        public string? ErrorMessage { get; set; } = null;

        /// <summary>
        /// Output bytes returned to the caller.
        /// </summary>
        public long OutputBytes { get; set; } = 0;

        /// <summary>
        /// Whether the output was truncated.
        /// </summary>
        public bool OutputTruncated { get; set; } = false;

        /// <summary>
        /// First part of the scrubbed output (at most 4096 characters).
        /// </summary>
        public string? OutputPreview { get; set; } = null;

        /// <summary>
        /// Execution duration in milliseconds (excluding approval wait).
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Trace identifier.
        /// </summary>
        public string? TraceId { get; set; } = null;

        /// <summary>
        /// Correlation identifier (request or turn).
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc
        {
            get
            {
                return _CreatedUtc;
            }
            set
            {
                _CreatedUtc = TimestampHelper.Normalize(value);
            }
        }

        /// <summary>
        /// Last update timestamp (UTC).
        /// </summary>
        public DateTime LastUpdateUtc
        {
            get
            {
                return _LastUpdateUtc;
            }
            set
            {
                _LastUpdateUtc = TimestampHelper.Normalize(value);
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.NewToolInvocationId();
        private string _TenantId = String.Empty;
        private string _ToolName = String.Empty;
        private DateTime _CreatedUtc = DateTime.UtcNow;
        private DateTime _LastUpdateUtc = DateTime.UtcNow;

        #endregion
    }
}
