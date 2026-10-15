namespace Hugo.Core.Responses
{
    using System;
    using Hugo.Connectors.Abstractions;
    using Hugo.Core.Enums;

    /// <summary>
    /// What the tool invocation pipeline decided and returned for one call (plan section 8.6). Every surface receives this
    /// from ToolInvocationService.InvokeAsync. Not thread-safe.
    /// </summary>
    public class ToolInvocationOutcome
    {
        #region Public-Members

        /// <summary>
        /// Tool invocation record identifier (null for a dry run).
        /// </summary>
        public string? InvocationId { get; set; } = null;

        /// <summary>
        /// Whether only steps 1 to 6 ran.
        /// </summary>
        public bool DryRun { get; set; } = false;

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
        /// Profile identifier.
        /// </summary>
        public string? ProfileId { get; set; } = null;

        /// <summary>
        /// Binding alias.
        /// </summary>
        public string? BindingAlias { get; set; } = null;

        /// <summary>
        /// Resource identifier.
        /// </summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>
        /// Connector type.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Decision.
        /// </summary>
        public InvocationDecisionEnum Decision { get; set; } = InvocationDecisionEnum.Denied;

        /// <summary>
        /// Stable reason code (allowed, readonly_binding, approval_required, tool_denied, binding_deny_pattern,
        /// tenant_lockdown, binding_not_found, tool_not_found, invalid_argument, forbidden, model_required, rejected, expired,
        /// cancelled, and so on).
        /// </summary>
        public string? DecisionCode { get; set; } = null;

        /// <summary>
        /// Human-readable reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Classified effect, when classification ran.
        /// </summary>
        public EffectEnum? Effect { get; set; } = null;

        /// <summary>
        /// Classifier reason.
        /// </summary>
        public string? EffectReason { get; set; } = null;

        /// <summary>
        /// Effective access mode.
        /// </summary>
        public AccessModeEnum? AccessMode { get; set; } = null;

        /// <summary>
        /// Approval identifier, when approval was involved.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Approval status, when approval was involved.
        /// </summary>
        public ApprovalStatusEnum? ApprovalStatus { get; set; } = null;

        /// <summary>
        /// Approval summary.
        /// </summary>
        public string? ApprovalSummary { get; set; } = null;

        /// <summary>
        /// When a pending approval expires.
        /// </summary>
        public DateTime? ApprovalExpiresUtc { get; set; } = null;

        /// <summary>
        /// Execution status.
        /// </summary>
        public InvocationStatusEnum Status { get; set; } = InvocationStatusEnum.NotExecuted;

        /// <summary>
        /// Scrubbed, bounded result the caller sees. For calls that did not run, an explanatory error result.
        /// </summary>
        public ToolResult? Result { get; set; } = null;

        /// <summary>
        /// Execution time in milliseconds, excluding approval waits.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Approval wait in milliseconds.
        /// </summary>
        public long ApprovalWaitMs { get; set; } = 0;

        #endregion

        #region Private-Members

        private string _ToolName = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the tool ran.
        /// </summary>
        /// <returns>True for Allowed and Approved calls that executed.</returns>
        public bool Executed()
        {
            return Status == InvocationStatusEnum.Succeeded || Status == InvocationStatusEnum.Failed || Status == InvocationStatusEnum.TimedOut;
        }

        #endregion
    }
}
