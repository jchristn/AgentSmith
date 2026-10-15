namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Outcome of <see cref="AgentRuntime.RunTurnAsync"/>.
    /// </summary>
    public class AgentTurnResult
    {
        #region Public-Members

        /// <summary>
        /// Turn ID.
        /// </summary>
        public string TurnId { get; set; } = String.Empty;

        /// <summary>
        /// Conversation ID.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Final visible answer (think blocks removed). Never null.
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
        /// Reasoning captured across the turn, when any.
        /// </summary>
        public string? Thinking { get; set; } = null;

        /// <summary>
        /// Messages appended to the conversation by this turn, in order (user, assistant, tool, final assistant).
        /// Never null.
        /// </summary>
        public List<AgentMessage> Messages
        {
            get
            {
                return _Messages;
            }
            set
            {
                _Messages = value ?? new List<AgentMessage>();
            }
        }

        /// <summary>
        /// Tool calls, in request order. Never null.
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
        /// Input tokens across all model calls of the turn (including compaction).
        /// </summary>
        public long TokensIn { get; set; } = 0;

        /// <summary>
        /// Output tokens across all model calls of the turn (including compaction).
        /// </summary>
        public long TokensOut { get; set; } = 0;

        /// <summary>
        /// Whether any token count was estimated.
        /// </summary>
        public bool TokensEstimated { get; set; } = false;

        /// <summary>
        /// Milliseconds from turn start to the first streamed token (answer or thinking); -1 when none.
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = -1;

        /// <summary>
        /// Turn duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Model iterations run.
        /// </summary>
        public int Iterations { get; set; } = 0;

        /// <summary>
        /// Why the turn ended.
        /// </summary>
        public AgentFinishReasonEnum FinishReason { get; set; } = AgentFinishReasonEnum.Completed;

        /// <summary>
        /// Error code when <see cref="FinishReason"/> is Error.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// Error message (no secrets) when <see cref="FinishReason"/> is Error.
        /// </summary>
        public string? ErrorMessage { get; set; } = null;

        /// <summary>
        /// Whether the history was compacted during this turn.
        /// </summary>
        public bool Compacted { get; set; } = false;

        /// <summary>
        /// Tools in the catalog.
        /// </summary>
        public int ToolsAvailable { get; set; } = 0;

        /// <summary>
        /// Tools offered to the model (after retrieval).
        /// </summary>
        public int ToolsOffered { get; set; } = 0;

        /// <summary>
        /// Whether embedding-based tool retrieval selected the offered tools.
        /// </summary>
        public bool ToolRetrievalUsed { get; set; } = false;

        /// <summary>
        /// Whether the fallback endpoint answered any iteration.
        /// </summary>
        public bool UsedFallback { get; set; } = false;

        /// <summary>
        /// Model that produced the final answer.
        /// </summary>
        public string Model { get; set; } = String.Empty;

        /// <summary>
        /// Pending approval ID when <see cref="FinishReason"/> is ApprovalPending.
        /// </summary>
        public string? PendingApprovalId { get; set; } = null;

        #endregion

        #region Private-Members

        private string _FinalText = String.Empty;
        private List<AgentMessage> _Messages = new List<AgentMessage>();
        private List<AgentToolCallRecord> _ToolCalls = new List<AgentToolCallRecord>();

        #endregion
    }
}
