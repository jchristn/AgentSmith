namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of asking an agent profile (POST /v1.0/api/agent-profiles/{id}/ask).
    /// </summary>
    public class AgentDelegationResult
    {
        #region Public-Members

        /// <summary>
        /// The agent's final answer.
        /// </summary>
        public string? FinalText { get; set; } = null;

        /// <summary>
        /// Conversation the turn ran in (pass it back to continue).
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Completed, ApprovalPending, or Failed.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Pending approval identifier when Status is ApprovalPending.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Short trace of the tool calls the agent made.
        /// </summary>
        public List<string> ToolTrace
        {
            get
            {
                return _ToolTrace;
            }
            set
            {
                _ToolTrace = value ?? new List<string>();
            }
        }

        #endregion

        #region Private-Members

        private List<string> _ToolTrace = new List<string>();

        #endregion
    }
}
