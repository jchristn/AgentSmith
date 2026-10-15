namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A stored message in a conversation (GET /v1.0/api/conversations/{id}/messages).
    /// </summary>
    public class ConversationMessage
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Turn that produced the message.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// Position in the conversation, starting at 1.
        /// </summary>
        public long Sequence { get; set; } = 0;

        /// <summary>
        /// User, Assistant, Tool, Summary, or System.
        /// </summary>
        public string? Role { get; set; } = null;

        /// <summary>
        /// Content.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Thinking content, when the model exposed it.
        /// </summary>
        public string? ThinkingContent { get; set; } = null;

        /// <summary>
        /// Tool calls requested by an assistant message.
        /// </summary>
        public List<ConversationToolCall> ToolCalls
        {
            get
            {
                return _ToolCalls;
            }
            set
            {
                _ToolCalls = value ?? new List<ConversationToolCall>();
            }
        }

        /// <summary>
        /// Call identifier a tool message answers.
        /// </summary>
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Tool name of a tool message.
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Model endpoint that produced an assistant message.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Input tokens.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Time to first token, milliseconds.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Duration, milliseconds.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Finish reason.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Whether the message was folded into a summary by compaction.
        /// </summary>
        public bool Compacted { get; set; } = false;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<ConversationToolCall> _ToolCalls = new List<ConversationToolCall>();

        #endregion
    }
}
