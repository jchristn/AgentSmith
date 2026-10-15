namespace Hugo.Server.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of a task delegated to Hugo's own agent loop (ask_agent_smith, plan section 13.5).
    /// </summary>
    public class AgentDelegationResult
    {
        #region Public-Members

        /// <summary>
        /// Final answer text (empty while an approval is pending, or when the turn failed without an answer).
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
        /// Conversation the turn ran in; pass it back to continue the conversation.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Outcome. Default Completed.
        /// </summary>
        public AgentDelegationStatusEnum Status { get; set; } = AgentDelegationStatusEnum.Completed;

        /// <summary>
        /// Approval the turn waits for, when Status is ApprovalPending.
        /// </summary>
        public string? ApprovalId { get; set; } = null;

        /// <summary>
        /// Compact trace of the tool calls the turn made, one line per call. Never null.
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

        private string _FinalText = String.Empty;
        private List<string> _ToolTrace = new List<string>();

        #endregion
    }
}
