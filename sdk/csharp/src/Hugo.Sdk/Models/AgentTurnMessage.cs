namespace Hugo.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A message produced during an agent turn.
    /// </summary>
    public class AgentTurnMessage
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation identifier.
        /// </summary>
        public string ConversationId { get; set; } = String.Empty;

        /// <summary>
        /// Turn identifier.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// User, Assistant, Tool, Summary, or System.
        /// </summary>
        public string Role { get; set; } = String.Empty;

        /// <summary>
        /// Content.
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Thinking content, when the model exposed it.
        /// </summary>
        public string? Thinking { get; set; } = null;

        /// <summary>
        /// Tool calls requested by an assistant message.
        /// </summary>
        public List<AgentTurnMessageToolCall> ToolCalls
        {
            get
            {
                return _ToolCalls;
            }
            set
            {
                _ToolCalls = value ?? new List<AgentTurnMessageToolCall>();
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
        /// Input tokens.
        /// </summary>
        public long TokensIn { get; set; } = 0;

        /// <summary>
        /// Output tokens.
        /// </summary>
        public long TokensOut { get; set; } = 0;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private List<AgentTurnMessageToolCall> _ToolCalls = new List<AgentTurnMessageToolCall>();

        #endregion
    }
}
