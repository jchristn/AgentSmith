namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of <see cref="IAgentDelegationService.AskAsync"/>: the final answer, the conversation to continue, and a
    /// compact tool trace. Not thread-safe.
    /// </summary>
    public class AgentDelegationResult
    {
        #region Public-Members

        /// <summary>
        /// Outcome.
        /// </summary>
        public AgentDelegationStatusEnum Status { get; set; } = AgentDelegationStatusEnum.Completed;

        /// <summary>
        /// Final answer text (think blocks removed). For ApprovalPending, the explanation of the pending approval. Never
        /// null.
        /// </summary>
        public string FinalText
        {
            get
            {
                return _FinalText;
            }
            set
            {
                _FinalText = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Conversation the turn ran in; pass it back to continue.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Turn identifier.
        /// </summary>
        public string TurnId { get; set; } = String.Empty;

        /// <summary>
        /// Agent profile identifier.
        /// </summary>
        public string ProfileId { get; set; } = String.Empty;

        /// <summary>
        /// Pending approval identifier when <see cref="Status"/> is ApprovalPending.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Why the turn ended: completed, max_iterations, tool_call_limit, approval_pending, cancelled, or error.
        /// </summary>
        public string FinishReason { get; set; } = "completed";

        /// <summary>
        /// Tool calls in request order. Never null.
        /// </summary>
        public List<AgentDelegationToolStep> ToolTrace
        {
            get
            {
                return _ToolTrace;
            }
            set
            {
                _ToolTrace = value ?? new List<AgentDelegationToolStep>();
            }
        }

        /// <summary>
        /// Input tokens used by the turn.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens used by the turn.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Turn duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Error code when <see cref="Status"/> is Failed.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message (no secrets) when <see cref="Status"/> is Failed.
        /// </summary>
        public string? ErrorMessage { get; set; } = null;

        #endregion

        #region Private-Members

        private string _FinalText = String.Empty;
        private List<AgentDelegationToolStep> _ToolTrace = new List<AgentDelegationToolStep>();

        #endregion
    }
}
