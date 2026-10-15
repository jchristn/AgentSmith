namespace Hugo.Sdk.Responses
{
    using System;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Decision and result of a tool invoke or dry run (preview).
    /// </summary>
    public class ToolInvocationOutcome
    {
        #region Public-Members

        /// <summary>
        /// Invocation Identifier.
        /// </summary>
        public string? InvocationId { get; set; } = null;

        /// <summary>
        /// Dry run.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Profile Identifier.
        /// </summary>
        public string? ProfileId { get; set; } = null;

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
        /// Allowed, Denied, ApprovalRequired, and so on.
        /// </summary>
        public string? Decision { get; set; } = null;

        /// <summary>
        /// Decision code.
        /// </summary>
        public string? DecisionCode { get; set; } = null;

        /// <summary>
        /// Reason.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Effect.
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
        /// Approval Identifier.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Approval status.
        /// </summary>
        public string? ApprovalStatus { get; set; } = null;

        /// <summary>
        /// Approval summary.
        /// </summary>
        public string? ApprovalSummary { get; set; } = null;

        /// <summary>
        /// Approval expires UTC.
        /// </summary>
        public DateTime? ApprovalExpiresUtc { get; set; } = null;

        /// <summary>
        /// Status.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Result.
        /// </summary>
        public ToolResult? Result { get; set; } = null;

        /// <summary>
        /// Duration milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Approval wait milliseconds.
        /// </summary>
        public long ApprovalWaitMs { get; set; } = 0;

        #endregion
    }
}
