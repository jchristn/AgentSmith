namespace Hugo.Server.Services
{
    using System;
    using Hugo.Core.Agent;
    using Hugo.Core.Enums;
    using Hugo.Core.Security;

    /// <summary>
    /// One tool call handed to <see cref="ToolInvocationService.InvokeAsync"/> by any surface (chat, MCP, A2A,
    /// OpenAI-compatible, REST). The surface has authenticated the caller and resolved the profile (D-018). Not
    /// thread-safe.
    /// </summary>
    public class InvocationRequest
    {
        #region Public-Members

        /// <summary>
        /// Authenticated caller.
        /// </summary>
        public RequestContext Principal { get; set; } = new RequestContext();

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
        /// Profile identifier. Required unless AdminResourceInvoke is true.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Binding alias. When null, the resource argument of the arguments is used.
        /// </summary>
        public string? ResourceAlias { get; set; } = null;

        /// <summary>
        /// Resource identifier, an alternative to the alias (REST).
        /// </summary>
        public string? ResourceId { get; set; } = null;

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
        /// Arguments JSON object (may include the resource argument).
        /// </summary>
        public string? ArgumentsJson { get; set; } = null;

        /// <summary>
        /// Surface.
        /// </summary>
        public InvocationSurfaceEnum Surface { get; set; } = InvocationSurfaceEnum.Rest;

        /// <summary>
        /// Run steps 1 to 6 only (resolve, authorize, validate, classify, rules) and record nothing.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// How to wait when approval is required.
        /// </summary>
        public ApprovalWaitModeEnum WaitMode { get; set; } = ApprovalWaitModeEnum.NoWait;

        /// <summary>
        /// Longest wait for WaitWithTimeout (for example Approvals.McpWaitSeconds).
        /// </summary>
        public TimeSpan? WaitTimeout { get; set; } = null;

        /// <summary>
        /// Receives approval transitions while waiting (chat emits approval.required and approval.resolved).
        /// </summary>
        public IAgentApprovalListener? ApprovalListener { get; set; } = null;

        /// <summary>
        /// Invoke a resource directly without a profile (requires Execute on Resource; the access mode is the
        /// resource ceiling, or ReadWriteWithApproval when none is set).
        /// </summary>
        public bool AdminResourceInvoke { get; set; } = false;

        /// <summary>
        /// Conversation, when the call came from a turn.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Message, when known.
        /// </summary>
        public string? MessageId { get; set; } = null;

        /// <summary>
        /// Correlation identifier (request or turn).
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        #endregion

        #region Private-Members

        private string _TenantId = String.Empty;
        private string _ToolName = String.Empty;

        #endregion
    }
}
