namespace Hugo.Core.Agent
{
    using System;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Outcome of one tool call as returned by <see cref="IAgentToolExecutor"/>: the scrubbed, bounded
    /// <see cref="ToolResult"/> plus the pipeline's classification and decision.
    /// </summary>
    public class AgentToolExecution
    {
        #region Public-Members

        /// <summary>
        /// Result the model sees. For a denied, rejected, expired, or pending call this is an explanatory result
        /// (usually <see cref="ToolResult.IsError"/> true). Never null.
        /// </summary>
        public ToolResult Result
        {
            get
            {
                return _Result;
            }
            set
            {
                _Result = value ?? new ToolResult();
            }
        }

        /// <summary>
        /// Classified effect. Default Read.
        /// </summary>
        public EffectEnum Effect { get; set; } = EffectEnum.Read;

        /// <summary>
        /// Decision. Default Allowed.
        /// </summary>
        public AgentToolDecisionEnum Decision { get; set; } = AgentToolDecisionEnum.Allowed;

        /// <summary>
        /// Stable reason code for the decision (for example <c>readonly_binding</c>), if any.
        /// </summary>
        public string? DecisionCode { get; set; } = null;

        /// <summary>
        /// Approval ID when an approval was involved.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Approval summary when an approval was involved.
        /// </summary>
        public string? ApprovalSummary { get; set; } = null;

        /// <summary>
        /// Resolved resource alias, when known.
        /// </summary>
        public string? Resource { get; set; } = null;

        /// <summary>
        /// Connector type that served the call, when known.
        /// </summary>
        public string? ConnectorType { get; set; } = null;

        /// <summary>
        /// Tool invocation record ID written by the pipeline, when known.
        /// </summary>
        public string? InvocationId { get; set; } = null;

        /// <summary>
        /// Execution duration in milliseconds, excluding approval wait. 0 when not executed.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Approval wait in milliseconds. 0 when no approval was involved.
        /// </summary>
        public long ApprovalWaitMs { get; set; } = 0;

        #endregion

        #region Private-Members

        private ToolResult _Result = new ToolResult();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the tool actually ran.
        /// </summary>
        /// <returns>True for Allowed and Approved.</returns>
        public bool Executed()
        {
            return Decision == AgentToolDecisionEnum.Allowed || Decision == AgentToolDecisionEnum.Approved;
        }

        #endregion
    }
}
