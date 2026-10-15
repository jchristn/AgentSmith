namespace Hugo.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Agent;
    using Hugo.Core.Helpers;

    /// <summary>
    /// A persisted conversation message (messages, plan 7.5). Assistant messages that requested tools carry their calls;
    /// tool messages carry the call they answer. The final assistant message of a turn carries the turn's time to first
    /// token, duration, and finish reason. Not thread-safe.
    /// </summary>
    public class ConversationMessage
    {
        #region Public-Members

        /// <summary>
        /// Identifier (prefix msg_).
        /// </summary>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.New(Constants.MessageIdPrefix) : value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string TenantId
        {
            get
            {
                return _TenantId;
            }
            set
            {
                _TenantId = value ?? String.Empty;
            }
        }

        /// <summary>
        /// Conversation identifier.
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
        /// Position in the conversation, starting at 1. Assigned by the store.
        /// </summary>
        public long Sequence { get; set; } = 0;

        /// <summary>
        /// Role.
        /// </summary>
        public AgentMessageRoleEnum Role { get; set; } = AgentMessageRoleEnum.User;

        /// <summary>
        /// Content. Tool messages hold the wrapped (scrubbed) tool output. Never null.
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
        /// Reasoning captured from think blocks, when any.
        /// </summary>
        public string? ThinkingContent { get; set; } = null;

        /// <summary>
        /// Tool calls requested by an assistant message. Never null.
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
        /// Input tokens attributed to the message.
        /// </summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>
        /// Output tokens attributed to the message.
        /// </summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>
        /// Time to first token of the turn in milliseconds (final assistant message only), or null.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Turn duration in milliseconds (final assistant message only), or null.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Why the turn ended (final assistant message only): completed, max_iterations, tool_call_limit,
        /// approval_pending, cancelled, or error. Null on other messages.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Whether a compaction summary replaced this message in the active history.
        /// </summary>
        public bool Compacted { get; set; } = false;

        /// <summary>
        /// Creation time (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.New(Constants.MessageIdPrefix);
        private string _TenantId = String.Empty;
        private string _ConversationId = String.Empty;
        private string _Content = String.Empty;
        private List<ConversationToolCall> _ToolCalls = new List<ConversationToolCall>();

        #endregion
    }
}
