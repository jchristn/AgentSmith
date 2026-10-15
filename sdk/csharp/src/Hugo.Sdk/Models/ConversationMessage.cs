namespace Hugo.Sdk.Models
{
    using System;

    /// <summary>
    /// A message in a conversation (preview).
    /// </summary>
    public class ConversationMessage
    {
        #region Public-Members

        /// <summary>
        /// Identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Conversation Identifier.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// Tenant Identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// System, User, Assistant, or Tool.
        /// </summary>
        public string? Role { get; set; } = null;

        /// <summary>
        /// Content.
        /// </summary>
        public string? Content { get; set; } = null;

        /// <summary>
        /// Thinking content.
        /// </summary>
        public string? ThinkingContent { get; set; } = null;

        /// <summary>
        /// Sequence.
        /// </summary>
        public int Sequence { get; set; } = 0;

        /// <summary>
        /// Model endpoint Identifier.
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
        /// Time to first token milliseconds.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Duration milliseconds.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Finish reason.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Creation timestamp, UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
