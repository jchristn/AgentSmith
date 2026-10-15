namespace Hugo.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Hugo.Sdk.Models;

    /// <summary>
    /// Result of one agent turn (POST /v1.0/api/conversations/{id}/messages without streaming).
    /// </summary>
    public class AgentTurnResult
    {
        #region Public-Members

        /// <summary>
        /// Turn identifier.
        /// </summary>
        public string TurnId { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Final assistant text.
        /// </summary>
        public string? FinalText { get; set; } = null;

        /// <summary>
        /// Thinking content, when the model exposed it.
        /// </summary>
        public string? Thinking { get; set; } = null;

        /// <summary>
        /// Messages produced during the turn.
        /// </summary>
        public List<AgentTurnMessage> Messages
        {
            get
            {
                return _Messages;
            }
            set
            {
                _Messages = value ?? new List<AgentTurnMessage>();
            }
        }

        /// <summary>
        /// Tool calls made during the turn.
        /// </summary>
        public List<AgentToolCallRecord> ToolCalls
        {
            get
            {
                return _ToolCalls;
            }
            set
            {
                _ToolCalls = value ?? new List<AgentToolCallRecord>();
            }
        }

        /// <summary>
        /// Input tokens.
        /// </summary>
        public long TokensIn { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long TokensOut { get; set; } = 0;

        /// <summary>
        /// Whether token counts are estimates.
        /// </summary>
        public bool TokensEstimated { get; set; } = false;

        /// <summary>
        /// Time to first token, milliseconds (-1 when unknown).
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = 0;

        /// <summary>
        /// Turn duration, milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Loop iterations.
        /// </summary>
        public int Iterations { get; set; } = 0;

        /// <summary>
        /// Completed, MaxIterations, ToolCallLimit, ApprovalPending, Cancelled, or Error.
        /// </summary>
        public string FinishReason { get; set; } = String.Empty;

        /// <summary>
        /// Error code when the turn failed.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message when the turn failed.
        /// </summary>
        public string? ErrorMessage { get; set; } = null;

        /// <summary>
        /// Whether the history was compacted during the turn.
        /// </summary>
        public bool Compacted { get; set; } = false;

        /// <summary>
        /// Tools available to the profile.
        /// </summary>
        public int ToolsAvailable { get; set; } = 0;

        /// <summary>
        /// Tools offered to the model.
        /// </summary>
        public int ToolsOffered { get; set; } = 0;

        /// <summary>
        /// Whether tool retrieval narrowed the tools offered.
        /// </summary>
        public bool ToolRetrievalUsed { get; set; } = false;

        /// <summary>
        /// Whether the fallback inference endpoint answered.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// Model that answered.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Approval the turn is waiting on, when FinishReason is ApprovalPending.
        /// </summary>
        public string? PendingApprovalId { get; set; } = null;

        #endregion

        #region Private-Members

        private List<AgentTurnMessage> _Messages = new List<AgentTurnMessage>();
        private List<AgentToolCallRecord> _ToolCalls = new List<AgentToolCallRecord>();

        #endregion
    }
}
