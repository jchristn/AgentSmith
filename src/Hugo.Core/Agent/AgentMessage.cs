namespace Hugo.Core.Agent
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One conversation message as the runtime reads and writes it through <see cref="IConversationStore"/>.
    /// Maps onto the <c>messages</c> table (P6.1), with tool calls as a child table.
    /// </summary>
    public class AgentMessage
    {
        #region Public-Members

        /// <summary>
        /// Message ID. The runtime assigns one (<c>msg_</c> prefix) when empty; a store may replace it.
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Conversation ID.
        /// </summary>
        public string ConversationId
        {
            get
            {
                return _ConversationId;
            }
            set
            {
                _ConversationId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Turn that produced the message, when known.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// Role.
        /// </summary>
        public AgentMessageRoleEnum Role { get; set; } = AgentMessageRoleEnum.User;

        /// <summary>
        /// Text content. For tool messages, the wrapped tool output. Never null.
        /// </summary>
        public string Content
        {
            get
            {
                return _Content;
            }
            set
            {
                _Content = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Reasoning captured from think blocks (assistant messages), when any.
        /// </summary>
        public string? Thinking { get; set; } = null;

        /// <summary>
        /// Tool calls requested (assistant messages). Never null.
        /// </summary>
        public List<AgentMessageToolCall> ToolCalls
        {
            get
            {
                return _ToolCalls;
            }
            set
            {
                _ToolCalls = value ?? new List<AgentMessageToolCall>();
            }
        }

        /// <summary>
        /// Call ID this message answers (tool messages).
        /// </summary>
        public string? ToolCallId { get; set; } = null;

        /// <summary>
        /// Tool name (tool messages).
        /// </summary>
        public string? ToolName { get; set; } = null;

        /// <summary>
        /// Input tokens attributed to this message (assistant messages: the prompt of the iteration).
        /// </summary>
        public long TokensIn { get; set; } = 0;

        /// <summary>
        /// Output tokens attributed to this message (assistant messages).
        /// </summary>
        public long TokensOut { get; set; } = 0;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = String.Empty;
        private string _ConversationId = String.Empty;
        private string _Content = String.Empty;
        private List<AgentMessageToolCall> _ToolCalls = new List<AgentMessageToolCall>();

        #endregion
    }
}
